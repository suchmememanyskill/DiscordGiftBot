﻿using System.Collections.Concurrent;
using Discord.WebSocket;
using DiscordGiftBot.Services.UserSpecificGuildStorage;
using Newtonsoft.Json;

namespace DiscordGiftBot.Services.Gift;

public record GiftClaimReceipt(GiftEntry Gift, DateTime ClaimedAt);

public class GiftService : BaseService<List<GiftEntry>>
{
    public override string StoragePath() => "./gifts.json";
    private DiscordSocketClient client;
    public List<SteamApp> SteamApps { get; private set; }
    private List<GiftCarrier> _cachedGifts;
    private ConcurrentDictionary<GiftEntry, DateTime> _giftClaimReceipts = new();
    private Timer _giftClearerTimer;

    public GiftService(DiscordSocketClient client)
    {
        this.client = client;
        Load();
        GetCombinedGifts();
        new Thread(x => GetSteamApps()).Start();
        _giftClearerTimer = new Timer(x =>
        {
            List<GiftEntry> toBeRemoved = new();

            foreach (var (key, value) in _giftClaimReceipts)
            {
                if ((value + TimeSpan.FromMinutes(10)) > DateTime.Now)
                    toBeRemoved.Add(key);
            }
            
            toBeRemoved.ForEach(RemoveClaimFromGift);
            Console.WriteLine($"Cleared {toBeRemoved.Count} expired claims");
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(60));
    }

    public async Task AddSteamKey(ulong serverId, ulong userId, string username, long steamId, string key, bool needApproval)
    {
        SteamApp? app = SteamApps.Find(x => x.AppId == steamId);

        if (app == null)
            throw new Exception("Steam game not found");
        
        storage.Add(new GiftEntry()
        {
            GameId = app.AppId,
            Type = GiftType.Steam,
            GameName = app.Name,
            GameKey = key,
            UserId = userId,
            Username = username,
            ServerLock = serverId,
            NeedApproval = needApproval
        });
        await Save();
        GetCombinedGifts();
    }

    public async Task AddCustomKey(ulong serverId, ulong userId, string username, string gameName, string key, bool needApproval)
    {
        GiftEntry gift = new()
        {
            Type = GiftType.Custom,
            GameName = gameName,
            GameKey = key,
            UserId = userId,
            Username = username,
            ServerLock = serverId,
            NeedApproval = needApproval
        };
        
        GiftCarrier? carrier = _cachedGifts.Find(x =>
            String.Equals(x.GameName, gameName, StringComparison.CurrentCultureIgnoreCase));
        
        if (carrier is {GiftType: GiftType.Custom})
        {
            gift.GameId = carrier.GameId;
            gift.GameName = carrier.GameName;
        }

        storage.Add(gift);
        await Save();
        GetCombinedGifts();
    }
    
    public async Task AddSteamKey(ulong serverId, ulong userId, string username, string steamGameName, string key, bool needApproval)
    {
        SteamApp? app = SteamApps.Find(x => String.Equals(x.Name, steamGameName, StringComparison.CurrentCultureIgnoreCase));
        
        if (app == null)
            throw new Exception("Steam game not found");

        await AddSteamKey(serverId, userId, username, app.AppId, key, needApproval);
    }

    public GiftEntry? GetGiftViaId(long id) => storage.Find(x => x.Id == id);

    /* Reserves a gift for a person. Returns True on successful claim, False on unsuccessful claim */
    public bool ClaimGift(GiftEntry gift)
    {
        if (_giftClaimReceipts.ContainsKey(gift))
        {
            if ((_giftClaimReceipts[gift] + TimeSpan.FromMinutes(5)) > DateTime.Now)
                return false;
        }
        
        _giftClaimReceipts[gift] = DateTime.Now;
        return true;
    }

    public void RemoveClaimFromGift(GiftEntry gift) => _giftClaimReceipts.Remove(gift, out DateTime _);

    public async Task RemoveKey(GiftEntry gift)
    {
        storage.Remove(gift);
        await Save();
        
        GetCombinedGifts();
    }

    public List<GiftEntry> GetAllGiftsOfUser(ulong userId) => storage.Where(x => x.UserId == userId).ToList();

    private List<GiftCarrier> GetCombinedGifts()
    {
        List<GiftCarrier> gifts = new();

        foreach (GiftEntry entry in storage)
        {
            GiftCarrier? carrier = gifts.Find(x => x.GameId == entry.GameId);

            if (carrier == null)
            {
                carrier = new(entry.GameName, entry.GetProperGameText(), entry.GameId, entry.Type);
                gifts.Add(carrier);
            }
            
            carrier.Gifts.Add(entry);
        }

        _cachedGifts = gifts;
        return gifts;
    }

    private void GetSteamApps()
    {
        using (HttpClient client = new())
        {
            var result = client.GetAsync(new Uri("https://api.steampowered.com/ISteamApps/GetAppList/v2/")).GetAwaiter().GetResult();
            string textResponse = result.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            SteamGames games = JsonConvert.DeserializeObject<SteamGames>(textResponse);
            SteamApps = games.AppList.Apps;
            Console.WriteLine($"Loaded {SteamApps.Count} steam games");
        }
    }

    public List<GiftCarrier> GetCarriersForServer(ulong serverId)
    {
        return _cachedGifts.Select(x => new GiftCarrier(x.GameName, x.GameText, x.GameId, x.GiftType)
        {
            Gifts = x.Gifts.Where(y => y.ServerLock == 0 || y.ServerLock == serverId).ToList()
        }).Where(x => x.Gifts.Count > 0).ToList();
    }
    
    public async Task<List<string>> AddKeys(ulong serverId, ulong userId, string username, string userInput, bool needApproval)
    {
        List<string> failed = new();
        List<string> lines = userInput.Split("\n").Select(x => x.Trim()).ToList();

        foreach (var line in lines)
        {
            if (!line.Contains(':'))
            {
                failed.Add(line);
                continue;
            }

            string key = line.Split(':').Last();
            string game = line.Substring(0, line.Length - key.Length - 1).Trim();
            key = key.Trim();
            SteamApp? app = SteamApps.Find(x => x.Name.Equals(game, StringComparison.InvariantCultureIgnoreCase));

            GiftEntry gift = new()
            {
                GameId = app?.AppId ?? Program.Random.Next(),
                Type = (app != null) ? GiftType.Steam : GiftType.Custom,
                GameName = app?.Name ?? game,
                GameKey = key,
                UserId = userId,
                Username = username,
                ServerLock = serverId,
                NeedApproval = needApproval
            };
            storage.Add(gift);
        }
        
        await Save();
        GetCombinedGifts();

        return failed;
    }
}