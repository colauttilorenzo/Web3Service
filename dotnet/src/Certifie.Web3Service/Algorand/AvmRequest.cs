using System;
using System.Collections.Generic;
using System.Numerics;

namespace Certifie.Web3.Algorand
{
    /// <summary>A box an application call reads: the application (0 for the called one) and the box name.</summary>
    internal sealed class BoxReference
    {
        public BoxReference(BigInteger appIndex, byte[] name) => (AppIndex, Name) = (appIndex, name);

        public BigInteger AppIndex { get; }
        public byte[] Name { get; }
    }

    /// <summary>The accounts, applications, assets and boxes an application call reads, found by simulation.</summary>
    internal sealed class Resources
    {
        public static readonly Resources None = new Resources(new List<string>(), new List<BigInteger>(), new List<BigInteger>(), new List<BoxReference>());

        public Resources(List<string> accounts, List<BigInteger> apps, List<BigInteger> assets, List<BoxReference> boxes) =>
            (Accounts, Apps, Assets, Boxes) = (accounts, apps, assets, boxes);

        public List<string> Accounts { get; }
        public List<BigInteger> Apps { get; }
        public List<BigInteger> Assets { get; }
        public List<BoxReference> Boxes { get; }
    }

    /// <summary>The fee of the last transaction of the group, and the resources it needs.</summary>
    internal sealed class BuildPlan
    {
        public BuildPlan(BigInteger fee, Resources? resources = null) => (Fee, Resources) = (fee, resources ?? Resources.None);

        public BigInteger Fee { get; }
        public Resources Resources { get; }
    }

    /// <summary>A group of transactions to sign and send, built for a sender. The last transaction is the one the result describes.</summary>
    internal sealed class AvmRequest
    {
        public AvmRequest(TransactionOptions? options, bool callsApplication, Func<string, SuggestedParams, BuildPlan, List<UnsignedTransaction>> build,
            Func<string, string>? explain = null)
        {
            (Options, CallsApplication, _build, _explain) = (options ?? new TransactionOptions(), callsApplication, build, explain ?? (failure => failure));
        }

        private readonly Func<string, SuggestedParams, BuildPlan, List<UnsignedTransaction>> _build;
        private readonly Func<string, string> _explain;

        public TransactionOptions Options { get; }

        /// <summary>True when the group calls an application, so simulation must find its resources and inner transactions.</summary>
        public bool CallsApplication { get; }

        public List<UnsignedTransaction> Build(string sender, SuggestedParams parameters, BuildPlan plan) => _build(sender, parameters, plan);

        /// <summary>The reason of a failed simulation, with the contract's error message when it has one.</summary>
        public string Explain(string failure) => _explain(failure);
    }
}
