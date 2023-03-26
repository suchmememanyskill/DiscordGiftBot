using System.Text;
using Discord;
using Discord.WebSocket;

namespace DiscordGiftBot.Services.Games;

public class NumberGuessService
{
    private CommandHandler _messages;
    private readonly DiscordSocketClient _client;

    private bool _active = false;
    private string _rewardMessage = "";
    private DateTime _end = DateTime.Now;
    private long _number = 0;

    private long _closestOffset = 0;
    private ulong _winningUserId = 0;
    private ulong _targetChannel = 0;

    private long _min = 0;
    private long _max = 0;

    public NumberGuessService(CommandHandler messages, DiscordSocketClient client)
    {
        _messages = messages;
        _client = client;
    }

    public void StartGame(string rewardMessage, TimeSpan timeout, ulong targetChannel, long minNumber = 0, long maxNumber = 500)
    {
        if (timeout > TimeSpan.FromHours(1))
            throw new Exception("Timeout max length is 1 hour");

        if (_active)
            throw new Exception("Number game is already ongoing!");

        if (minNumber < 0 || maxNumber < minNumber)
            throw new Exception("Entered number ranges are invalid");
        
        _active = true;
        _rewardMessage = rewardMessage;
        _number = Random.Shared.NextInt64(minNumber, maxNumber + 1);
        _closestOffset = long.MaxValue;
        _winningUserId = 0;
        _targetChannel = targetChannel;
        _end = DateTime.Now + timeout;
        _min = minNumber;
        _max = maxNumber;
        _messages.OnMessage += ProcessMessage;
        
        Console.WriteLine($"Number is {_number}");
    }

    public async Task ForceStopGame()
    {
        _active = false;
        _messages.OnMessage -= ProcessMessage;
    }
    
    private async Task ProcessMessage(SocketUserMessage message)
    {
        if (!_active)
            return;

        if (message.Channel.Id != _targetChannel)
            return;
        
        if (_end < DateTime.Now)
        {
            await FinalizeGame(message);
            return;
        }

        StringBuilder builder = new();

        foreach (char c in message.Content)
        {
            if ("1234567890".Contains(c))
                builder.Append(c);
        }

        string possibleNumber = builder.ToString();

        if (possibleNumber.Length <= 0)
            return;
        
        if (!long.TryParse(possibleNumber, out long l))
            return;

        if (l < _min || l > _max)
            return;

        long offset = Math.Abs(_number - l);

        if (offset < _closestOffset && _closestOffset != 0)
        {
            Console.WriteLine($"Game Progressed: {_closestOffset} -> {offset}, by {message.Author.Username}");
            _closestOffset = offset;
            _winningUserId = message.Author.Id;

            if (offset == 0)
                await FinalizeGame(message);
        }
    }

    private async Task FinalizeGame(SocketUserMessage message)
    {
        _active = false;
        _messages.OnMessage -= ProcessMessage;
        
        if (_winningUserId != 0)
        {
            string guessAccuracy = (_closestOffset == 0) ? $"The hidden number was exactly {_number}." : $"The hidden number was {_number}. The user was {_closestOffset} off!";
            await message.Channel.SendMessageAsync($"User <@{_winningUserId}> won the number guessing game! {guessAccuracy}");

            try
            {
                IUser user = await _client.GetUserAsync(_winningUserId);
                IDMChannel channel = await user.CreateDMChannelAsync();
                await channel.SendMessageAsync(_rewardMessage);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to dm user: {e.Message}");
                await message.Channel.SendMessageAsync($"Failed to send message to <@{_winningUserId}>");
            }
        }
    }
}