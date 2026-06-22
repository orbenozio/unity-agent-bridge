// McpToolContext.cs — deferred replies for ASYNC tools (SPEC §8: run_playmode, run_tests).
//
// Most tools return a value and ToolRegistry replies immediately. Async tools (enter
// Play Mode for N seconds; run the test runner) finish via a callback LATER. Such a
// tool declares a trailing `McpToolContext ctx = null` parameter; ToolRegistry injects
// it and does NOT auto-reply — the tool calls ctx.Complete(...) / ctx.Fail(...) when
// the operation finishes. The reply fires exactly once.

using System.Threading;
using Newtonsoft.Json;

namespace UnityAgentBridge.Editor
{
    public sealed class McpToolContext
    {
        private readonly string _id;
        private readonly System.Action<string> _reply;
        private int _replied;

        public McpToolContext(string id, System.Action<string> reply)
        {
            _id = id;
            _reply = reply;
        }

        /// <summary>Send the success response. No-op if a reply was already sent.</summary>
        public void Complete(object result)
        {
            if (Interlocked.Exchange(ref _replied, 1) != 0) return;
            _reply(Protocol.Ok(_id, JsonConvert.SerializeObject(result ?? new { })));
        }

        /// <summary>Send a failure response. No-op if a reply was already sent.</summary>
        public void Fail(string error)
        {
            if (Interlocked.Exchange(ref _replied, 1) != 0) return;
            _reply(Protocol.Error(_id, error));
        }
    }
}
