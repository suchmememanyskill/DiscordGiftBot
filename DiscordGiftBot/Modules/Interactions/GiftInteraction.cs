﻿using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordGiftBot.Modules.Base;
using DiscordGiftBot.Modules.SlashCommands;
using DiscordGiftBot.Services.Gift;

namespace DiscordGiftBot.Modules.Interactions;

public class GiftInteraction : SlashCommandBase
{
    public GiftService GiftService { get; set; }
    protected bool forceEphemeral = false;
    private IBaseInterface me => this;

    private RequestOptions _requestOptions = new()
    {
        RetryMode = RetryMode.RetryRatelimit,
        RatelimitCallback = x => Task.Delay(1000)
    };

    public async Task RespondEphemeral(string text = null, Embed embed = null, MessageComponent components = null)
    {
        if (!forceEphemeral && Context.Interaction is SocketMessageComponent)
        {
            SocketMessageComponent interaction = Context.Interaction as SocketMessageComponent;
            await interaction.UpdateAsync(x =>
            {
                x.Content = text;
                x.Embed = embed;
                x.Components = components;
            });
        }
        else await RespondAsync(text, embed: embed, components: components, ephemeral:true);
    }
    
    [ComponentInteraction("giftmenu:*")]
    public async Task GiftMenu(string _, params string[] selections)
    {
        string selection = selections.First();
        long gameId = long.Parse(selection);

        GiftCarrier? carrier = GiftService.GetCarriersForServer(Context.Guild.Id).Find(x => x.GameId == gameId);
        if (carrier == null)
            return;

        var componentBuilder = new ComponentBuilder();
        
        carrier.Users.ForEach(x => componentBuilder.WithButton( x.Games.First().NeedApproval ? $"Ask {x.Name}" : $"Get from {x.Name}", $"giftget:{selection}:{x.Id.ToString()}"));
        
        string gameCount = (carrier.Gifts.Count > 1) ? $"are {carrier.Gifts.Count} copies" : "is 1 copy";
        string text = $"There {gameCount} of the game {carrier.GameName} available.";
        if (carrier.GiftType == GiftType.Steam)
            text += "\nSteam link: " + carrier.GameText;

        await RespondAsync(text, allowedMentions: AllowedMentions.None, components: componentBuilder.Build(), ephemeral: true);
    }

    [ComponentInteraction("giftget:*:*")]
    public async Task GiftGet(string gameIdStr, string userIdStr)
    {
        await DeferAsync(true);
        ulong userId = ulong.Parse(userIdStr);
        long gameId = long.Parse(gameIdStr);
        
        GiftCarrier? carrier = GiftService.GetCarriersForServer(Context.Guild.Id).Find(x => x.GameId == gameId);

        GiftUser? user = carrier?.Users.Find(x => x.Id == userId);
        if (user == null)
            return;

        if (user.Games.Count < 1)
            return;

        GiftEntry gift = user.Games.First();

        IUser giftOwner = await Context.Client.GetUserAsync(userId);
        var giftOwnerDm = await giftOwner.CreateDMChannelAsync();

        if (!gift.NeedApproval)
        {
            if (!GiftService.ClaimGift(gift))
            {
                await FollowupAsync("Unable to claim gift. Please try again", ephemeral: true, options: _requestOptions);
                return;
            }
            
            try
            {
                var giftReceiverDm = await Context.User.CreateDMChannelAsync();
                await giftReceiverDm.SendMessageAsync(
                    $"Key of {gift.GameName}, gifted by {giftOwner.Mention} ({giftOwner.Username}#{giftOwner.Discriminator}): `{gift.GameKey}`", options: _requestOptions);
                await GiftService.RemoveKey(gift);
                await FollowupAsync(
                    "Claimed! See your DMs for the game key. Don't forget to thank the person for the free game!", ephemeral: true, options: _requestOptions);
                await giftOwnerDm.SendMessageAsync($"Key of {gift.GameName} was claimed by {Context.User.Mention} ({Context.User.Username}#{Context.User.Discriminator})", options: _requestOptions);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                GiftService.RemoveClaimFromGift(gift);
                await FollowupAsync($"Failed to claim game. Are your DMs open with the bot? '{e.Message}'", ephemeral: true, options: _requestOptions);
            }

            return;
        }

        var componentBuilder = new ComponentBuilder()
            .WithButton("Yes", $"giftaccept:{gift.Id}:{Context.User.Id}")
            .WithButton("No", $"giftdeny:{Context.User.Id}");

        await giftOwnerDm.SendMessageAsync(
            $"{Context.User.Mention} ({Context.User.Username}#{Context.User.Discriminator} from server {Context.Guild.Name}) wants to have the game {gift.GameName}. Do you want to gift this game?",
            components: componentBuilder.Build(), options: _requestOptions);
        
        await FollowupAsync($"Asked {giftOwner.Mention} ({giftOwner.Username}#{giftOwner.Discriminator}) for the game {gift.GameName}. Please have your DM's open with the bot to be able to receive a response", ephemeral: true, options: _requestOptions);
    }

    [ComponentInteraction("giftdeny:*")]
    public async Task GiftDeny(string userIdStr)
    {
        ulong userId = ulong.Parse(userIdStr);
        IUser discordUser = await Context.Client.GetUserAsync(userId);
        var channel = await discordUser.CreateDMChannelAsync();
        await channel.SendMessageAsync($"{Context.User.Mention} ({Context.User.Username}#{Context.User.Discriminator}) has denied your gift");
        await RespondEphemeral($"Denied gift");
    }

    [ComponentInteraction("giftaccept:*:*")]
    public async Task GiftAccept(string giftIdStr, string userIdStr)
    {
        ulong userId = ulong.Parse(userIdStr);
        long giftId = long.Parse(giftIdStr);
        
        GiftEntry? gift = GiftService.GetGiftViaId(giftId);
        
        if (gift == null || gift.UserId != Context.User.Id)
            return;
        
        if (!GiftService.ClaimGift(gift))
        {
            await RespondEphemeral("Unable to claim gift. Please try again");
            return;
        }

        try
        {
            IUser discordUser = await Context.Client.GetUserAsync(userId);
            var channel = await discordUser.CreateDMChannelAsync();
            await channel.SendMessageAsync(
                $"Key of {gift.GameName}, gifted by {Context.User.Mention} ({Context.User.Username}#{Context.User.Discriminator}): `{gift.GameKey}`\nDon't forget to thank the person for the free game!");
            await GiftService.RemoveKey(gift);
            await RespondEphemeral(
                $"Accepted gift of {gift.GameName} to {discordUser.Mention} ({discordUser.Username}#{discordUser.Discriminator}). Thanks!");
        }
        catch (Exception e)
        {
            GiftService.RemoveClaimFromGift(gift);
            await RespondEphemeral($"Something went wrong during claiming the gift. '{e.Message}'");
        }
    }
    
    [ModalInteraction("gift_modal_add")]
    public async Task GiftAddModal(GiftSlashCommands.GiftModal modal)
    {
        List<string> failed = await GiftService.AddKeys((modal.KeepToServer.ToLower() == "yes") ? me.Guild().Id : 0, me.User().Id,
            me.User().Username, modal.Keys, modal.NeedConfirmation.ToLower() == "yes");

        await RespondAsync($"Added keys\n\n{failed.Count} lines failed to get added\n{string.Join('\n', failed)}",
            ephemeral: true);
    }
}