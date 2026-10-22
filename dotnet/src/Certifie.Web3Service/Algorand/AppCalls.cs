using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Certifie.Web3.Algorand
{
    /// <summary>A call of an ARC-4 method, or a bare call without arguments when Fn is null; AppId 0 with a creation creates the application.</summary>
    internal sealed class AppCall
    {
        public AppCall(BigInteger appId, Arc4Function? fn, IReadOnlyList<CallArgument> args, Creation? creation = null) =>
            (AppId, Fn, Args, Creation) = (appId, fn, args, creation);

        public BigInteger AppId { get; }
        public Arc4Function? Fn { get; }
        public IReadOnlyList<CallArgument> Args { get; }
        public Creation? Creation { get; }
    }

    /// <summary>Application calls and creations as transaction groups: the payment of a payable method first, then the call.</summary>
    internal static class AppCalls
    {
        private static readonly Arc4Uint Uint8 = new Arc4Uint(8, "uint8");

        public static AvmRequest Request(AppCall call, TransactionOptions? options = null)
        {
            var payment = PaymentOf(call, options ?? new TransactionOptions());
            return new AvmRequest(options, true, (sender, parameters, plan) =>
                PaymentTransactions(call, sender, parameters, payment).Append(CallTransaction(call, sender, parameters, plan)).ToList(),
                failure => call.Fn == null ? failure : ProgramErrors.Explain(call.Fn.Errors, failure));
        }

        /// <summary>The application arguments of a call; references to accounts other than the sender count from 1.</summary>
        public static List<byte[]> ApplicationArgs(AppCall call) => Encode(call, "").AppArgs;

        /// <summary>A payment to the application account, e.g. to fund the boxes it creates.</summary>
        public static AvmRequest Fund(BigInteger appId, BigInteger amount, TransactionOptions? options = null) =>
            new AvmRequest(options, false, (sender, parameters, plan) => new List<UnsignedTransaction>
            {
                new UnsignedTransaction("pay", sender, plan.Fee, parameters, TransactionFields.Payment(AlgorandAddress.OfApplication(appId), amount)),
            });

        private static UnsignedTransaction CallTransaction(AppCall call, string sender, SuggestedParams parameters, BuildPlan plan)
        {
            var (appArgs, references) = Encode(call, sender);
            var fields = TransactionFields.ApplicationCall(call.AppId, appArgs, Merge(call.AppId, references, plan.Resources), call.Creation);
            return new UnsignedTransaction("appl", sender, plan.Fee, parameters, fields);
        }

        private static IEnumerable<UnsignedTransaction> PaymentTransactions(AppCall call, string sender, SuggestedParams parameters, BigInteger? amount)
        {
            if (amount == null) yield break;
            var receiver = AlgorandAddress.OfApplication(call.AppId);
            yield return new UnsignedTransaction("pay", sender, parameters.MinFee, parameters, TransactionFields.Payment(receiver, amount.Value));
        }

        /// <summary>options.Value pays a payable method; other methods take no coins.</summary>
        private static BigInteger? PaymentOf(AppCall call, TransactionOptions options)
        {
            var value = Integers.RequireNonNegative(options.Value ?? BigInteger.Zero, "value");
            var payable = call.Args.Any(arg => arg.Kind == "payment");
            if (payable && call.AppId.IsZero) throw Errors.InvalidArgument("A payable method cannot create the application; fund it after deployment with options.Value.");
            if (!payable && value > 0) throw Errors.InvalidArgument($"{call.Fn?.Method.Signature ?? "The call"} is not payable; options.Value cannot be sent.");
            return payable ? value : (BigInteger?)null;
        }

        /// <summary>Account 0 is the sender and application 0 the called application, so their references need no foreign entry.</summary>
        private static (List<byte[]> AppArgs, Resources References) Encode(AppCall call, string sender)
        {
            var references = new Resources(new List<string>(), new List<BigInteger>(), new List<BigInteger>(), new List<BoxReference>());
            var values = call.Args.Where(arg => arg.Kind != "payment").Select(arg => ValueOf(arg, call.AppId, sender, references)).ToList();
            return (call.Fn == null ? new List<byte[]>() : Arc4Methods.Encode(call.Fn, values), references);
        }

        /// <summary>A value to encode; references are uint8 indexes into the foreign arrays.</summary>
        private static (Arc4Type Type, object Value) ValueOf(CallArgument arg, BigInteger appId, string sender, Resources references) => arg.Kind switch
        {
            "value" => (arg.Type!, arg.Value!),
            "account" => (Uint8, (BigInteger)((string)arg.Value! == sender ? 0 : IndexOf(references.Accounts, (string)arg.Value!) + 1)),
            "asset" => (Uint8, (BigInteger)IndexOf(references.Assets, (BigInteger)arg.Value!)),
            _ => (Uint8, (BigInteger)((BigInteger)arg.Value! == appId ? 0 : IndexOf(references.Apps, (BigInteger)arg.Value!) + 1)),
        };

        /// <summary>The references of the arguments keep their positions; the resources found by simulation follow, with the applications of their boxes.</summary>
        private static Resources Merge(BigInteger appId, Resources references, Resources found)
        {
            var boxApps = found.Boxes.Select(box => box.AppIndex).Where(app => !app.IsZero && app != appId);
            return new Resources(references.Accounts.Concat(found.Accounts).Distinct().ToList(), references.Apps.Concat(found.Apps).Concat(boxApps).Distinct().ToList(),
                references.Assets.Concat(found.Assets).Distinct().ToList(), found.Boxes);
        }

        private static int IndexOf<T>(List<T> items, T item)
        {
            if (!items.Contains(item)) items.Add(item);
            return items.IndexOf(item);
        }
    }
}
