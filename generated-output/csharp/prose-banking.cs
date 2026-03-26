using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Console.WriteLine(Codex_prose_banking.main());

public abstract record Transactionresult;

public sealed record Success(long Field0) : Transactionresult;
public sealed record Insufficientfunds(long Field0, long Field1) : Transactionresult;

public sealed record Account(string owner, long balance);

public static class Codex_prose_banking
{
    public static Account deposit(long amount, Account account)
    {
        return new Account(account.owner, (account.balance + amount));
    }

    public static TransactionResult withdraw(long amount, Account account)
    {
        return ((account.balance >= amount) ? new Success((account.balance - amount)) : new InsufficientFunds(amount, account.balance));
    }

    public static string main()
    {
        return "Banking module loaded";
    }

}
