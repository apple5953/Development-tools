using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace BEAI.QueryHost.Core
{
    /// <summary>
    /// How a registered tool affects the model. The Reality Bridge still opens no Transaction itself;
    /// a tool that needs scratch geometry (temporary views, temporary links) must roll everything back
    /// inside its own module and declare <see cref="TemporaryRollback"/> so clients and audits can see it.
    /// </summary>
    public static class RealityModelEffect
    {
        /// <summary>Reads the model only.</summary>
        public const string None = "none";

        /// <summary>Creates temporary elements inside a transaction that is always rolled back.</summary>
        public const string TemporaryRollback = "temporary-rollback";
    }

    /// <summary>Describes one analysis tool contributed by a DevelopmentTools module.</summary>
    public sealed class RealityToolDescriptor
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string ModelEffect { get; set; }
        public int TimeoutSeconds { get; set; }
        public bool RequiresDocument { get; set; }
    }

    /// <summary>
    /// In-process registry for module analysis tools served through the one Reality Bridge.
    /// Only code running inside Revit (DevelopmentTools.Addin at startup) can register a tool; nothing
    /// arriving over HTTP can add, replace or name a handler, so the production allowlist stays explicit.
    /// Handlers run on the Revit UI thread inside the existing ExternalEvent queue. Arguments arrive as a JSON
    /// object string so module assemblies do not need to share this assembly's JSON library.
    /// </summary>
    public static class RealityToolRegistry
    {
        public const int DefaultTimeoutSeconds = 30;
        public const int MaxTimeoutSeconds = 600;

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, Entry> Tools = new Dictionary<string, Entry>(StringComparer.Ordinal);

        private sealed class Entry
        {
            public RealityToolDescriptor Descriptor;
            public Func<UIApplication, Document, string, RealityToolResult> Handler;
        }

        public static void Register(RealityToolDescriptor descriptor, Func<UIApplication, Document, string, RealityToolResult> handler)
        {
            if (descriptor == null) throw new ArgumentNullException("descriptor");
            if (handler == null) throw new ArgumentNullException("handler");
            string name = (descriptor.Name ?? "").Trim().ToLowerInvariant();
            if (name.Length == 0 || name.IndexOf('.') <= 0) throw new ArgumentException("Tool name must be '<area>.<action>'.", "descriptor");
            if (name.StartsWith("system.", StringComparison.Ordinal)) throw new ArgumentException("system.* tools are reserved.", "descriptor");
            if (descriptor.ModelEffect != RealityModelEffect.None && descriptor.ModelEffect != RealityModelEffect.TemporaryRollback)
                throw new ArgumentException("Registered tools may not change the model.", "descriptor");

            descriptor.Name = name;
            descriptor.TimeoutSeconds = Math.Max(DefaultTimeoutSeconds, Math.Min(descriptor.TimeoutSeconds, MaxTimeoutSeconds));
            lock (SyncRoot)
            {
                if (Tools.ContainsKey(name)) throw new InvalidOperationException("Tool already registered: " + name);
                Tools[name] = new Entry { Descriptor = descriptor, Handler = handler };
            }
            Logger.Info(string.Format("[RealityToolRegistry] Registered {0} ({1}, {2}s).", name, descriptor.ModelEffect, descriptor.TimeoutSeconds));
        }

        public static bool TryGet(string name, out RealityToolDescriptor descriptor, out Func<UIApplication, Document, string, RealityToolResult> handler)
        {
            lock (SyncRoot)
            {
                Entry entry;
                if (name != null && Tools.TryGetValue(name, out entry))
                {
                    descriptor = entry.Descriptor;
                    handler = entry.Handler;
                    return true;
                }
            }
            descriptor = null;
            handler = null;
            return false;
        }

        public static List<RealityToolDescriptor> List()
        {
            lock (SyncRoot)
            {
                return Tools.Values.Select(delegate(Entry e) { return e.Descriptor; }).OrderBy(delegate(RealityToolDescriptor d) { return d.Name; }).ToList();
            }
        }

        /// <summary>HTTP wait for a request body; registered long-running tools get their declared timeout.</summary>
        public static int TimeoutForBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return DefaultTimeoutSeconds;
            try
            {
                JToken tool = JObject.Parse(body)["tool"];
                RealityToolDescriptor descriptor;
                Func<UIApplication, Document, string, RealityToolResult> ignored;
                if (tool != null && TryGet(tool.ToString().Trim().ToLowerInvariant(), out descriptor, out ignored))
                    return descriptor.TimeoutSeconds;
            }
            catch (Exception)
            {
                // Malformed bodies are rejected later by the dispatcher with a stable error code.
            }
            return DefaultTimeoutSeconds;
        }
    }

    /// <summary>Result of a registered tool: structured data plus warnings; failures carry a BEAI error code.</summary>
    public sealed class RealityToolResult
    {
        public RealityToolResult()
        {
            Warnings = new List<string>();
        }

        public bool Success { get; set; }
        public object Data { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> Warnings { get; private set; }

        public static RealityToolResult Ok(object data)
        {
            return new RealityToolResult { Success = true, Data = data };
        }

        public static RealityToolResult Fail(string code, string message)
        {
            return new RealityToolResult { Success = false, ErrorCode = code, ErrorMessage = message };
        }
    }
}
