using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using SharpExchange.Chat.Actions;
using SharpExchange.Chat.Events;
using SharpExchange.Net.WebSockets;
using StackoverflowChatbot.Relay;

namespace StackoverflowChatbot
{
	internal static class Discord
	{
		private static DiscordSocketClient? _client;

		internal static async Task<DiscordSocketClient> GetDiscord() => _client ??= await CreateDiscordClient();

		internal static Dictionary<int, RoomWatcher<DefaultWebSocket>> StackRoomWatchers = new Dictionary<int, RoomWatcher<DefaultWebSocket>>();
		internal static Dictionary<int, ActionScheduler> StackSchedulers = new Dictionary<int, ActionScheduler>();

		private static async Task<DiscordSocketClient> CreateDiscordClient()
		{
			var client = new DiscordSocketClient();
			//Setuo handlers
			client.MessageReceived += ClientRecieved;
			//Logs in
			await client.LoginAsync(TokenType.Bot, Config.Manager.Config().DiscordToken);
			await client.StartAsync();
			//Now wr're done
			return client;
		}

		private static async Task ClientRecieved(SocketMessage arg)
		{

			if (arg.Author is SocketGuildUser user)
			{
				if (arg.Author.IsBot) return;

				var config = Config.Manager.Config();
				Console.WriteLine($"[DIS {arg.Channel.Name}] {arg.Content}");
				//Check if we have a mapping.
				if (config.DiscordToStackMap.ContainsKey(arg.Channel.Name))
				{
					//We are setup to map this channel's messages to stack.
					var roomId = config.DiscordToStackMap[arg.Channel.Name];
					//Build the message
					//var displayname = string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;

					// Create a list of messages in case there are embedded codeblocks or stuff alongside text
					var messages = FromDiscordExtensions.BuildSoMessage(user, config, arg);

					foreach (var message in messages)
					{
						//Find the room scheduler
						if (StackSchedulers.ContainsKey(roomId))
						{
							//We already have a scheduler, lets goooo
							var sched = StackSchedulers[roomId];
							await sched.CreateMessageAsync(message);
						}
						//Or create one if we already have a watcher.
						else if (StackRoomWatchers.ContainsKey(roomId))
						{
							var watcher = StackRoomWatchers[roomId];
							var newScheduler = new ActionScheduler(watcher.Auth, RoomService.Host, roomId);
							StackSchedulers.Add(roomId, newScheduler);
							await newScheduler.CreateMessageAsync(message);

							await arg.Channel.SendMessageAsync("Opened a new scheduler for sending messages to Stack. FYI.");
						}
						else
						{
							//or complain about not watching stack.
							await arg.Channel.SendMessageAsync(
								"Unable to sync messages to Stack - I'm not watching the corresponding channel. Invite me to the channel on stack and tryagain.");
						}
					}
				}
			}
		}

		private static string BuildSoMessage(SocketGuildUser user, Config.Base config, SocketMessage arg)
		{
			var displayname = string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
			var messageStart = $@"\[**[{displayname}]({config.DiscordInviteLink})**]";
			var messageContent = arg.Content!;
			foreach (var mentionedUser in arg.MentionedUsers)
			{
				messageContent = messageContent.Replace(mentionedUser.Mention, $"@{mentionedUser.Username}");
			}
			foreach (var mentionedRoles in arg.MentionedRoles)
			{
				messageContent = messageContent.Replace(mentionedRoles.Mention, $"[@{mentionedRoles.Name}]({config.DiscordInviteLink})");
			}
			foreach (var mentionedChannel in arg.MentionedChannels)
			{
				// Library doesn't provide channel mention string
				messageContent = messageContent.Replace($"<#{mentionedChannel.Id}>", $"[@{mentionedChannel.Name}]({config.DiscordInviteLink})");
			}

			var embeddedCode = Regex.Matches(messageContent, "```.+```", RegexOptions.Multiline);
			foreach (Match? codeBlock in embeddedCode)
			{
				if (codeBlock == null)
					continue;

				var soCodeBlock = codeBlock.ToString().Replace("\n", "\n    ");
				messageContent = messageContent.Replace(codeBlock.ToString(), soCodeBlock);
			}

			return messageStart + messageContent;
		}
	}
}
