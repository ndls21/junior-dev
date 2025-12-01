using System;
using System.Reflection;
using System.Linq;
using DevExpress.AIIntegration.WinForms.Chat;
using DevExpress.AIIntegration.Blazor.Chat;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            ShowHelp();
            return;
        }

        switch (args[0].ToLower())
        {
            case "messages":
            case "blazor":
                InspectBlazorChatMessage();
                break;
            case "events":
            case "control":
                InspectAIChatControl();
                break;
            case "all":
                InspectBlazorChatMessage();
                Console.WriteLine("\n" + new string('=', 50) + "\n");
                InspectAIChatControl();
                break;
            default:
                Console.WriteLine($"Unknown command: {args[0]}");
                ShowHelp();
                break;
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine("DevExpress AI Chat Inspector");
        Console.WriteLine("Usage: DevExpressInspector <command>");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  messages, blazor    Inspect BlazorChatMessage types and enums");
        Console.WriteLine("  events, control     Inspect AIChatControl events");
        Console.WriteLine("  all                 Inspect both messages and events");
        Console.WriteLine("  help, -h            Show this help");
    }

    static void InspectBlazorChatMessage()
    {
        Console.WriteLine("Inspecting BlazorChatMessage...");

        Type blazorChatMessageType = typeof(BlazorChatMessage);

        Console.WriteLine($"Type: {blazorChatMessageType.FullName}");

        Console.WriteLine("\n=== PROPERTIES ===");
        foreach (var prop in blazorChatMessageType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Console.WriteLine($"{prop.PropertyType.Name} {prop.Name}");
        }

        Console.WriteLine("\n=== CONSTRUCTORS ===");
        foreach (var ctor in blazorChatMessageType.GetConstructors())
        {
            Console.WriteLine($"Constructor: {string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}");
        }

        Console.WriteLine("\n=== ChatMessageRole ENUM ===");
        Type chatMessageRoleType = typeof(ChatMessageRole);
        if (chatMessageRoleType.IsEnum)
        {
            foreach (var value in Enum.GetValues(chatMessageRoleType))
            {
                Console.WriteLine($"  {value}");
            }
        }

        Console.WriteLine("\n=== Microsoft.Extensions.AI.ChatRole ENUM ===");
        Type msChatRoleType = typeof(Microsoft.Extensions.AI.ChatRole);
        if (msChatRoleType.IsEnum)
        {
            foreach (var value in Enum.GetValues(msChatRoleType))
            {
                Console.WriteLine($"  {value}");
            }
        }
    }

    static void InspectAIChatControl()
    {
        Console.WriteLine("Inspecting AIChatControl MessageSent event...");

        Type type = typeof(AIChatControl);

        var messageSentEvent = type.GetEvent("MessageSent");
        if (messageSentEvent != null)
        {
            Console.WriteLine($"MessageSent event type: {messageSentEvent.EventHandlerType}");

            // Check if it's a generic EventHandler
            if (messageSentEvent.EventHandlerType.IsGenericType)
            {
                var genericArgs = messageSentEvent.EventHandlerType.GenericTypeArguments;
                Console.WriteLine($"Generic args: {string.Join(", ", genericArgs.Select(t => t.FullName))}");

                if (genericArgs.Length > 0)
                {
                    var eventArgsType = genericArgs[0];
                    Console.WriteLine($"EventArgs type: {eventArgsType.FullName}");

                    Console.WriteLine("EventArgs properties:");
                    foreach (var prop in eventArgsType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        Console.WriteLine($"  {prop.PropertyType.Name} {prop.Name}");
                    }
                }
            }
        }

        Console.WriteLine("\n=== AIChatControl METHODS ===");
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.Contains("Message") || m.Name.Contains("Load") || m.Name.Contains("Send"))
            .OrderBy(m => m.Name);

        foreach (var method in methods)
        {
            var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Console.WriteLine($"{method.ReturnType.Name} {method.Name}({parameters})");
        }
    }
}