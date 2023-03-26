using Discord.Interactions;
using DiscordGiftBot.Modules.Base;
using DiscordGiftBot.Services.Games;

namespace DiscordGiftBot.Modules.SlashCommands;

[Group("game", "Interact with the key game system")]
public class GameSlashCommand : SlashCommandBase
{
    private NumberGuessService _numberGame;
    private IBaseInterface me => this;

    public GameSlashCommand(NumberGuessService numberGame)
    {
        _numberGame = numberGame;
    }

    [SlashCommand("number", "Start a number guessing game.")]
    public async Task StartNumberGame(string rewardMessage, TimeSpan? timeSpan = null, long minNumber = 0, long maxNumber = 500)
    {
        try
        {
            timeSpan ??= TimeSpan.FromMinutes(10);
            _numberGame.StartGame(rewardMessage, timeSpan.Value, Context.Channel.Id, minNumber, maxNumber);
            await Context.Channel.SendMessageAsync(
                $"Started a number guessing game! The game lasts for {timeSpan.Value.TotalMinutes:0} minutes. The number to guess is between {minNumber} and {maxNumber}");
            await me.RespondEphermeral("Game Created");
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            await _numberGame.ForceStopGame();
            throw;
        }
    }
}