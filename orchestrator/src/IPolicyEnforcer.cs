using System;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public interface IPolicyEnforcer
{
    PolicyResult CheckPolicy(ICommand command, SessionConfig sessionConfig);
}

public record PolicyResult(bool Allowed, string? Rule = null);
