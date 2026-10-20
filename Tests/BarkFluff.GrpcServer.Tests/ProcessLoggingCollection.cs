namespace BarkFluff.GrpcServer.Tests;

// Console, Serilog's static logger and SelfLog are shared with every test in the process.
[CollectionDefinition("Process logging", DisableParallelization = true)]
public sealed class ProcessLoggingCollection { }
