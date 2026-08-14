namespace Tyhp.TyhpLang.Enum
{
    /// <summary>
    /// How <c>foreach (await $expr as …)</c> should be emitted (Story 11 Phase 9).
    /// Classified by the checker and consumed by the emitter.
    /// </summary>
    public enum AsyncForeachKind
    {
        /// <summary>Not an await-foreach (or classification failed).</summary>
        None = 0,

        /// <summary><c>$expr</c> is <c>AsyncIterable&lt;T&gt;</c> — while-loop with <c>_await(next/current)</c>.</summary>
        AsyncIterable = 1,

        /// <summary><c>$expr</c> is <c>Promise&lt;Iterable&lt;T&gt;&gt;</c> — <c>foreach (_await($expr) as …)</c>.</summary>
        PromiseIterable = 2,

        /// <summary><c>$expr</c> is <c>Promise&lt;AsyncIterable&lt;T&gt;&gt;</c> — await then async-iterate.</summary>
        PromiseAsyncIterable = 3,
    }
}
