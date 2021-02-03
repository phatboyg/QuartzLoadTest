using System;

namespace Scheduler
{
    public record Message
    {
        DateTime Created { get; init; }
    }
}