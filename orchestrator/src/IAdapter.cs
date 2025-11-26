using System.Threading.Tasks;
using JuniorDev.Contracts;

namespace JuniorDev.Orchestrator;

public interface IAdapter
{
    bool CanHandle(ICommand command);
    Task HandleCommand(ICommand command, SessionState session);
}