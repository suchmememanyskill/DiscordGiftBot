namespace DiscordGiftBot.Services.Gift;

public record GiftUser(string Name, ulong Id, List<GiftEntry> Games);

public class GiftCarrier
{
    public string GameName;
    public string GameText;
    public long GameId;
    public GiftType GiftType;
    public List<GiftEntry> Gifts = new();

    public List<GiftUser> Users
    {
        get
        {
            List<GiftUser> users = new();
            
            Gifts.ForEach(x =>
            {
                GiftUser? user = users.Find(y => y.Id == x.UserId);

                if (user == null)
                {
                    user = new(x.Username, x.UserId, new());
                    users.Add(user);
                }
                
                user.Games.Add(x);
            });

            return users;
        }
    }

    public GiftCarrier(string gameName, string gameText, long gameId, GiftType giftType)
    {
        GameName = gameName;
        GameText = gameText;
        GameId = gameId;
        GiftType = giftType;
    }
}