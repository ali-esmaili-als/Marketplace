using System;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Settlements;
using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Sellers;
using Moq;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class SettlementRequestValidationTests
{
    [Fact]
    public async Task Request_rejects_inactive_seller_before_opening_financial_transaction()
    {
        var seller = Seller.Create(10, 20); // Pending by default.
        var sellers = new Mock<ISellerManagementRepository>();
        sellers.Setup(x => x.GetSellerByUserIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(seller);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var service = new SettlementService(
            Mock.Of<ILifecycleRepository>(), uow.Object, Mock.Of<IIdGenerator>(),
            Mock.Of<ISellerPayoutGateway>(), sellers.Object);

        await Assert.ThrowsAsync<DomainException>(() => service.RequestAsync(20, 30, 100_000));

        uow.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Request_rejects_unverified_bank_account_without_reserving_funds()
    {
        var seller = Seller.Create(10, 20);
        seller.Activate();
        var account = SellerBankAccount.Create(30, seller.Id, "Test Bank", "IR0030", "Seller");
        var balance = SellerBalance.Create(40, seller.Id);
        balance.AddAvailable(500_000);

        var sellers = new Mock<ISellerManagementRepository>();
        sellers.Setup(x => x.GetSellerByUserIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(seller);

        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSellerBalanceAsync(seller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetSellerBankAccountAsync(seller.Id, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, Mock.Of<IIdGenerator>(),
            Mock.Of<ISellerPayoutGateway>(), sellers.Object);

        await Assert.ThrowsAsync<DomainException>(() => service.RequestAsync(20, account.Id, 100_000));

        Assert.Equal(500_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        lifecycle.Verify(x => x.AddSettlement(It.IsAny<Settlement>()), Times.Never);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(500001)]
    public async Task Request_rejects_nonpositive_or_excessive_amount_without_creating_settlement(long amount)
    {
        var seller = Seller.Create(10, 20);
        seller.Activate();
        var account = SellerBankAccount.Create(30, seller.Id, "Test Bank", "IR0030", "Seller");
        account.Verify();
        var balance = SellerBalance.Create(40, seller.Id);
        balance.AddAvailable(500_000);

        var sellers = new Mock<ISellerManagementRepository>();
        sellers.Setup(x => x.GetSellerByUserIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(seller);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSellerBalanceAsync(seller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetSellerBankAccountAsync(seller.Id, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, Mock.Of<IIdGenerator>(),
            Mock.Of<ISellerPayoutGateway>(), sellers.Object);

        await Assert.ThrowsAsync<DomainException>(() => service.RequestAsync(20, account.Id, amount));

        Assert.Equal(500_000, balance.AvailableIRR);
        Assert.Equal(0, balance.ReservedForSettlementIRR);
        lifecycle.Verify(x => x.AddSettlement(It.IsAny<Settlement>()), Times.Never);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Request_verified_account_reserves_funds_and_writes_settlement_ledger_entry()
    {
        var seller = Seller.Create(10, 20);
        seller.Activate();
        var account = SellerBankAccount.Create(30, seller.Id, "Test Bank", "IR0030", "Seller");
        account.Verify();
        var balance = SellerBalance.Create(40, seller.Id);
        balance.AddAvailable(500_000);

        var sellers = new Mock<ISellerManagementRepository>();
        sellers.Setup(x => x.GetSellerByUserIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(seller);
        var lifecycle = new Mock<ILifecycleRepository>();
        lifecycle.Setup(x => x.GetSellerBalanceAsync(seller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);
        lifecycle.Setup(x => x.GetSellerBankAccountAsync(seller.Id, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        Settlement? createdSettlement = null;
        BalanceTransaction? createdEntry = null;
        lifecycle.Setup(x => x.AddSettlement(It.IsAny<Settlement>()))
            .Callback<Settlement>(value => createdSettlement = value);
        lifecycle.Setup(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()))
            .Callback<BalanceTransaction>(value => createdEntry = value);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<SettlementResult>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<SettlementResult>> action, CancellationToken token) => action(token));
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        long nextId = 100;
        var ids = new Mock<IIdGenerator>();
        ids.Setup(x => x.NextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref nextId));

        var service = new SettlementService(
            lifecycle.Object, uow.Object, ids.Object,
            Mock.Of<ISellerPayoutGateway>(), sellers.Object);

        var result = await service.RequestAsync(20, account.Id, 200_000);

        Assert.Equal("Requested", result.Status);
        Assert.NotNull(createdSettlement);
        Assert.Equal(200_000, createdSettlement!.AmountIRR);
        Assert.Equal(SettlementStatus.Requested, createdSettlement.Status);
        Assert.Equal(500_000, balance.AvailableIRR);
        Assert.Equal(200_000, balance.ReservedForSettlementIRR);
        Assert.Equal(300_000, balance.WithdrawableIRR);

        Assert.NotNull(createdEntry);
        Assert.Equal(BalanceTransactionType.Settlement, createdEntry!.Type);
        Assert.Equal(createdSettlement.Id, createdEntry.SettlementId);
        Assert.Equal(200_000, createdEntry.AmountIRR);
        Assert.Equal(0, createdEntry.BalanceBeforeIRR);
        Assert.Equal(200_000, createdEntry.BalanceAfterIRR);
        Assert.Equal(BalanceBucket.ReservedForSettlement, createdEntry.Bucket);
        lifecycle.Verify(x => x.AddSettlement(It.IsAny<Settlement>()), Times.Once);
        lifecycle.Verify(x => x.AddBalanceTransaction(It.IsAny<BalanceTransaction>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
