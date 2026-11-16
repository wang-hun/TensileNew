using RingInspect.Api;

Console.WriteLine("RingInspect API 启动中…");
await RingInspectApiHost.Create(args).RunAsync();