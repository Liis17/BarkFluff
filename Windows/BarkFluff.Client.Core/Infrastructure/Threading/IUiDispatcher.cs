namespace BarkFluff.Client.Core.Infrastructure.Threading;

public interface IUiDispatcher
{
    void Post(Action action);
}
