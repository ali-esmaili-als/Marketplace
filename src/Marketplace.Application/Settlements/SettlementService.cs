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

        // Withdrawable balance is shared mutable financial state. Serialize the read/reserve/write
        // sequence so two simultaneous requests cannot reserve the same available funds.
        return await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            var balance=await _life.GetSellerBalanceAsync(seller.Id,token)??throw new DomainException("Seller balance not found.");
            var account=await _life.GetSellerBankAccountAsync(seller.Id,bankAccountId,token)??throw new DomainException("Bank account not found.");
            if(!account.IsVerified) throw new DomainException("Seller bank account is not verified.");
            if(amountIRR<=0 || amountIRR>balance.WithdrawableIRR) throw new DomainException("Settlement amount exceeds withdrawable balance.");

            var reservedBefore=balance.ReservedForSettlementIRR;
            balance.ReserveForSettlement(amountIRR);
            var settlement=Settlement.Create(await _ids.NextAsync(token),seller.Id,amountIRR,account.Id,account.BankName,account.Iban,account.AccountHolderName);
            _life.AddSettlement(settlement);

            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),seller.Id,null,settlement.Id,
                BalanceTransactionType.Settlement,amountIRR,reservedBefore,balance.ReservedForSettlementIRR,"SETTLEMENT_REQUESTED",BalanceBucket.ReservedForSettlement));

            await _uow.SaveChangesAsync(token);
            return new SettlementResult(settlement.Id,amountIRR,settlement.Status.ToString(),null);
        },ct);
    }

    public async Task<SettlementResult> ProcessAsync(long settlementId,CancellationToken ct=default)
    {
        long sellerId=0; long amount=0; string bankName="",iban="",holder="";
        SettlementResult? alreadyCompleted = null;

        // Claim the settlement under a serializable transaction. Only Requested -> Processing
        // may reach the external payout call; a concurrent admin request sees Processing and
        // fails the domain transition instead of starting a second transfer.
        await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            var settlement=await _life.GetSettlementAsync(settlementId,token)??throw new DomainException("Settlement not found.");
            if(settlement.Status==SettlementStatus.Completed)
            {
                alreadyCompleted = new SettlementResult(settlement.Id, settlement.AmountIRR, settlement.Status.ToString(), settlement.Reference);
                return 0;
            }

            settlement.MarkProcessing();
            sellerId=settlement.SellerId;
            amount=settlement.AmountIRR;
            bankName=settlement.BankNameSnapshot;
            iban=settlement.IbanSnapshot;
            holder=settlement.AccountHolderNameSnapshot;
            await _uow.SaveChangesAsync(token);
            return 0;
        },ct);

        // A repeated admin request for an already completed settlement must never initiate
        // another external bank transfer.
        if (alreadyCompleted is not null) return alreadyCompleted;

        // If the gateway throws or times out, keep the settlement in Processing and keep the
        // funds reserved. The external bank may have accepted the transfer despite a timeout;
        // automatically marking it Failed/releasing funds could permit a duplicate payout.
        // Reconciliation must resolve this state before any retry is allowed.
        (bool Success, string? Reference, string? Error) result;
        try
        {
            result = await _payout.TransferAsync(bankName,iban,holder,amount,ct);
        }
        catch
        {
            // A thrown exception/timeout is ambiguous. Once the provider call has returned
            // control to us, move to OnHold so reconciliation cannot race an in-flight payout.
            // Do not release the reserved balance; an operator must verify the bank's final state.
            await _uow.ExecuteInSerializableTransactionAsync(async token =>
            {
                var settlement = await _life.GetSettlementAsync(settlementId, token);
                if (settlement?.Status == SettlementStatus.Processing)
                {
                    settlement.PutOnHold();
                    await _uow.SaveChangesAsync(token);
                }
                return 0;
            }, CancellationToken.None);
            throw;
        }

        return await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            var settlement=await _life.GetSettlementAsync(settlementId,token)??throw new DomainException("Settlement not found.");
            var balance=await _life.GetSellerBalanceAsync(sellerId,token)??throw new DomainException("Seller balance not found.");
            if(settlement.Status==SettlementStatus.Completed)
                return new SettlementResult(settlement.Id,settlement.AmountIRR,settlement.Status.ToString(),settlement.Reference);

            if(!result.Success)
            {
                settlement.Fail(result.Error??"Payout failed.");
                var reservedBefore=balance.ReservedForSettlementIRR;
                balance.FailSettlement(amount);
                _life.AddBalanceTransaction(BalanceTransaction.Create(
                    await _ids.NextAsync(token),sellerId,null,settlement.Id,
                    BalanceTransactionType.SettlementFailed,amount,reservedBefore,balance.ReservedForSettlementIRR,
                    result.Error ?? "SETTLEMENT_FAILED",BalanceBucket.ReservedForSettlement));
                await _uow.SaveChangesAsync(token);
                return new SettlementResult(settlement.Id,amount,settlement.Status.ToString(),null);
            }

            settlement.Complete(result.Reference);
            var before=balance.AvailableIRR;
            balance.CompleteSettlement(amount);
            balance.RemoveAvailable(amount);
            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),sellerId,null,settlement.Id,
                BalanceTransactionType.Settlement,amount,before,balance.AvailableIRR,result.Reference,BalanceBucket.Available));

            await _uow.SaveChangesAsync(token);
            return new SettlementResult(settlement.Id,amount,settlement.Status.ToString(),result.Reference);
        },ct);
    }

    public async Task<SettlementResult> ReconcileAsync(long settlementId, bool transferCompleted, string? bankReference, string note, CancellationToken ct=default)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("A reconciliation note is required.");

        return await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            var settlement = await _life.GetSettlementAsync(settlementId, token)
                ?? throw new DomainException("Settlement not found.");
            if (settlement.Status != SettlementStatus.OnHold)
                throw new DomainException("Only ambiguous processing settlements can be reconciled.");

            var balance = await _life.GetSellerBalanceAsync(settlement.SellerId, token)
                ?? throw new DomainException("Seller balance not found.");
            var amount = settlement.AmountIRR;

            if (transferCompleted)
            {
                if (string.IsNullOrWhiteSpace(bankReference))
                    throw new DomainException("Bank reference is required when confirming a completed transfer.");

                settlement.Complete(bankReference);
                var before = balance.AvailableIRR;
                balance.CompleteSettlement(amount);
                balance.RemoveAvailable(amount);
                var auditReference = $"RECONCILED_PAID:{bankReference.Trim()}:{note.Trim()}";
                if (auditReference.Length > 200) auditReference = auditReference[..200];
                _life.AddBalanceTransaction(BalanceTransaction.Create(
                    await _ids.NextAsync(token), settlement.SellerId, null, settlement.Id,
                    BalanceTransactionType.Settlement, amount, before, balance.AvailableIRR,
                    auditReference, BalanceBucket.Available));
            }
            else
            {
                // Use only after checking the bank/provider's final status. A mere timeout is
                // not evidence that no transfer occurred.
                var reservedBefore = balance.ReservedForSettlementIRR;
                settlement.Fail($"Reconciled as not transferred: {note.Trim()}");
                balance.FailSettlement(amount);
                var auditReference = $"RECONCILED_NOT_PAID:{note.Trim()}";
                if (auditReference.Length > 200) auditReference = auditReference[..200];
                _life.AddBalanceTransaction(BalanceTransaction.Create(
                    await _ids.NextAsync(token), settlement.SellerId, null, settlement.Id,
                    BalanceTransactionType.SettlementFailed, amount, reservedBefore,
                    balance.ReservedForSettlementIRR, auditReference,
                    BalanceBucket.ReservedForSettlement));
            }

            await _uow.SaveChangesAsync(token);
            return new SettlementResult(settlement.Id, amount, settlement.Status.ToString(), settlement.Reference);
        }, ct);
    }

}
