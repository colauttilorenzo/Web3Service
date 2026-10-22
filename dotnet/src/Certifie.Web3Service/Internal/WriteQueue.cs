using System;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3
{
    /// <summary>Runs tasks one at a time, in call order. A failed task does not block the next one.</summary>
    internal sealed class WriteQueue
    {
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public async Task<T> RunAsync<T>(Func<Task<T>> task, CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { return await task().ConfigureAwait(false); }
            finally { _lock.Release(); }
        }
    }
}
