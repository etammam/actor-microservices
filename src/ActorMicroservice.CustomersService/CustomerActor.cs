using Akka.Actor;
using Akka.Event;

namespace ActorMicroservice.CustomersService;

public class CustomerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    public CustomerActor()
    {
        Receive<int>(message =>
        {
            _log.Info($"new customer in-query invoked: for customer id: {message}");
        });
    }
}