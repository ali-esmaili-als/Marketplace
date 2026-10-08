using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Sellers;

namespace Marketplace.Application.Settlements;

public sealed record SettlementResult(long SettlementId,long AmountIRR,string Status,string? Reference);

public sealed class SettlementService
{
    private readonly ILifecycleRepository _life;
    private readonly IUnitOfWork _uow;
    private readonly IIdGenerator _ids;
    private readonly ISellerPayoutGateway _payout;
    private readonly ISellerManagementRepository _sellers;

    public SettlementService(ILifecycleRepository life,IUnitOfWork uow,IIdGenerator ids,ISellerPayoutGateway payout,ISellerManagementRepository sellers)
    {
        _life=life; _uow=uow; _ids=ids; _payout=payout; _sellers=sellers;
    }

    public async Task<SettlementResult> RequestAsync(long userId,long bankAccountId,long amountIRR,CancellationToken ct=default)
    {
        var seller = await _sellers.GetSellerByUserIdAsync(userId, ct) ?? throw new DomainException("Seller profile not found.");
        if (seller.Status != SellerStatus.Active) throw new DomainException("Seller is not active.");
        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var balance=await _life.GetSellerBalanceAsync(seller.Id,token)??throw new DomainException("Seller balance not found.");
            var account=await _life.GetSellerBankAccountAsync(seller.Id,bankAccountId,token)??throw new DomainException("Bank account not found.");
            if(!account.IsVerified) throw new DomainException("Seller bank account is not verified.");
            if(amountIRR<=0 || amountIRR>balance.WithdrawableIRR) throw new DomainException("Settlement amount exceeds withdrawable balance.");

            balance.ReserveForSettlement(amountIRR);
            var settlement=Settlement.Create(await _ids.NextAsync(token),seller.Id,amountIRR,account.Id,account.BankName,account.Iban,account.AccountHolderName);
            _life.AddSettlement(settlement);

            var before=balance.WithdrawableIRR+amountIRR;
            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),seller.Id,null,settlement.Id,
                BalanceTransactionType.Settlement,amountIRR,before,balance.WithdrawableIRR,"SETTLEMENT_REQUESTED",BalanceBucket.ReservedForSettlement));

            await _uow.SaveChangesAsync(token);
            return new SettlementResult(settlement.Id,amountIRR,settlement.Status.ToString(),null);
        },ct);
    }

    public async Task<SettlementResult> ProcessAsync(long settlementId,CancellationToken ct=default)
    {
        long sellerId=0; long amount=0; string bankName="",iban="",holder="";
        SettlementResult? alreadyCompleted = null;
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var settlement=await _life.GetSettlementAsync(settlementId,token)??throw new DomainException("Settlement not found.");
            if(settlement.Status==SettlementStatus.Completed)
            {
                alreadyCompleted = new SettlementResult(settlement.Id, settlement.AmountIRR, settlement.Status.ToString(), settlement.Reference);
                return 0;
            }
            settlement.MarkProcessing();
            sellerId=settlement.SellerId; amount=settlement.AmountIRR; bankName=settlement.BankNameSnapshot; iban=settlement.IbanSnapshot; holder=settlement.AccountHolderNameSnapshot;
            await _uow.SaveChangesAsync(token);
            return 0;
        },ct);

        // A repeated admin request for an already completed settlement must never initiate
        // another external bank transfer.
        if (alreadyCompleted is not null) return alreadyCompleted;

        var result=await _payout.TransferAsync(bankName,iban,holder,amount,ct);

        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var settlement=await _life.GetSettlementAsync(settlementId,token)??throw new DomainException("Settlement not found.");
            var balance=await _life.GetSellerBalanceAsync(sellerId,token)??throw new DomainException("Seller balance not found.");
            if(settlement.Status==SettlementStatus.Completed)
                return new SettlementResult(settlement.Id,settlement.AmountIRR,settlement.Status.ToString(),settlement.Reference);

            if(!result.Success)
            {
                settlement.Fail(result.Error??"Payout failed.");
                balance.FailSettlement(amount);
                await _uow.SaveChangesAsync(token);
                return new SettlementResult(settlement.Id,amount,settlement.Status.ToString(),null);
            }

            settlement.Complete(result.Reference);
            var before=balance.WithdrawableIRR;
            balance.CompleteSettlement(amount);
            balance.RemoveAvailable(amount);
            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),sellerId,null,settlement.Id,
                BalanceTransactionType.Settlement,amount,before,balance.WithdrawableIRR,result.Reference,BalanceBucket.Available));

            await _uow.SaveChangesAsync(token);
            return new SettlementResult(settlement.Id,amount,settlement.Status.ToString(),result.Reference);
        },ct);
    }
}