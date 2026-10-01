using System.Collections.Generic;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;

namespace ACE.Server.Commands.AdminCommands;

public class FetchString
{
    /// <summary>
    /// The longest piece of a value that is sent as one message: the client can't show much more than this of a message
    /// </summary>
    private const int MaxLineLength = 200;

    [CommandHandler(
        "fetchstring",
        AccessLevel.Admin,
        CommandHandlerFlag.None,
        1,
        "Fetches a server property that is a string",
        "fetchstring (string)"
    )]
    public static void HandleFetchServerStringProperty(Session session, params string[] parameters)
    {
        var stringVal = PropertyManager.GetString(parameters[0], cacheFallback: false);

        // the value first, since it is what was asked for: a long description in front of it pushes it off the end of what the client shows
        foreach (var line in ToDisplayLines($"{parameters[0]}: {stringVal.Item}", MaxLineLength))
        {
            CommandHandlerHelper.WriteOutputInfo(session, line);
        }

        foreach (var line in ToDisplayLines($"({stringVal.Description ?? "No Description"})", MaxLineLength))
        {
            CommandHandlerHelper.WriteOutputInfo(session, line);
        }
    }

    /// <summary>
    /// Splits a text into pieces of at most maxLength characters, after a comma or a space where there is one to break at,
    /// so that nothing is lost and each piece fits in one message
    /// </summary>
    internal static IEnumerable<string> ToDisplayLines(string text, int maxLength)
    {
        text ??= "";

        while (text.Length > maxLength)
        {
            var cut = text.LastIndexOfAny(new[] { ',', ' ' }, maxLength - 1, maxLength);

            // no comma or space to break at (or one right at the start): break where it has to be
            cut = cut <= 0 ? maxLength : cut + 1;

            yield return text.Substring(0, cut);

            text = text.Substring(cut);
        }

        yield return text;
    }
}
