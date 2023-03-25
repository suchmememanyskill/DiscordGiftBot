using Newtonsoft.Json;

namespace DiscordGiftBot.Services.Gift;

public enum GiftType
{
    Custom = 0,
    Steam,
}

public class GiftEntry
{
    public long Id { get; set; } = Program.Random.Next();
    public long GameId { get; set; } = Program.Random.Next();
    [JsonIgnore]
    public GiftType Type { get => (GiftType) TypeInt; set => TypeInt = (int) value; }
    public int TypeInt { get; set; }
    public string GameName { get; set; }
    public string GameKey { get; set; }
    public ulong UserId { get; set; }
    public string Username { get; set; }
    public ulong ServerLock { get; set; }
    public bool NeedApproval { get; set; } = true;

    public string GetProperGameText()
    {
        if (Type == GiftType.Steam)
        {
            return $"https://store.steampowered.com/app/{GameId}";
        }

        return GameName;
    }

    public GiftEntry()
    {
    }
}