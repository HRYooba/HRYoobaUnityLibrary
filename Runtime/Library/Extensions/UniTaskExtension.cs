using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace HRYooba.Library
{
    public static class UniTaskExtension
    {
        public static UniTaskCompletionSource ToUniTaskCompletionSource(this UniTask self)
        {
            var utcs = new UniTaskCompletionSource();
            UniTask.Create(async () =>
            {
                try
                {
                    await self;
                    utcs.TrySetResult();
                }
                catch (OperationCanceledException)
                {
                    utcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    utcs.TrySetException(e);
                }
            }).Forget();

            return utcs;
        }
    }
}