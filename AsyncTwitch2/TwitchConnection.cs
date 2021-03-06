using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using WebSocketSharp;
using AsyncTwitch.Models;

namespace AsyncTwitch
{
    public class TwitchConnection : MonoBehaviour
    {
        public static TwitchConnection Instance;

        private static WebSocket _ws;
        private static System.Random _random = new System.Random();

        public static Dictionary<string, Models.RoomState> RoomStates = new Dictionary<string, Models.RoomState>();

        private static Dictionary<string, Action<RawMessage>> _handlers = new Dictionary<string, Action<RawMessage>>();

        #region Events

        public static UnityAction<TwitchConnection> OnConnected;
        public static UnityAction<TwitchConnection, TwitchMessage> OnMessage;
        public static UnityAction<TwitchConnection, string> OnRawMessage;
        public static UnityAction<TwitchConnection, RoomState> OnRoomStateChange;

        #endregion

        public static void OnLoad()
        {
            if (Instance != null)
                return;

            new GameObject("AsyncTwitch").AddComponent<TwitchConnection>();
        }

        public void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(this);
            Plugin.Debug("Created AsyncTwitch GameObject");

            _handlers.Add("PRIVMSG", Handlers.PRIVMSG);
            _handlers.Add("ROOMSTATE", Handlers.ROOMSTATE);

            _ws = new WebSocket("wss://irc-ws.chat.twitch.tv");

            _ws.OnOpen += (sender, ev) =>
            {
                Plugin.Debug("WebSocket Connection Opened");

                _ws.Send("CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership");
                
                if (Config.Username == string.Empty || Config.OAuthKey == string.Empty)
                {
                    int id = _random.Next(10000, 1000000);

                    _ws.Send($"NICK justinfan{id}");
                    _ws.Send($"PASS {id}");
                }
                else
                {
                    _ws.Send($"NICK {Config.Username}");
                    _ws.Send($"PASS {Config.OAuthKey}");
                }

                string channel = Config.ChannelName == string.Empty ? Config.Username : Config.ChannelName;
                if (channel != string.Empty)
                    _ws.Send($"JOIN #{channel}");

                OnConnected?.Invoke(this);
            };

            _ws.OnClose += (sender, ev) =>
            {
                Plugin.Debug($"Socket Closed with reason {ev.Reason}");
            };

            _ws.OnMessage += _ws_OnMessage;
            _ws.ConnectAsync();
        }

        private void _ws_OnMessage(object sender, MessageEventArgs ev)
        {
            if (!ev.IsText)
                return;

            string message = ev.Data.TrimEnd();

            #region Debug Printing
#if DEBUG
            string[] lines = message
                .Split('\n')
                .Select((line, i) =>
                {
                    string prefix = "Twitch";
                    string pre = i == 0 ? "Twitch" : new string(' ', prefix.Length);

                    return $"{pre} | {line}";
                })
                .ToArray();

            Console.WriteLine(string.Join("\n", lines));
#endif
            #endregion

            if (message.StartsWith("PING"))
            {
                Plugin.Debug("Recieved PING. Sending PONG...");
                _ws.Send("PONG :tmi.twitch.tv");

                return;
            }

            OnRawMessage?.Invoke(this, message);

            bool valid = Parsers.ValidRawMessage(message);
            if (!valid)
            {
                Plugin.Debug($"Unhandled message: {message}");
                return;
            }

            RawMessage rawMessage = Parsers.ParseRawMessage(message);
            
            if (_handlers.ContainsKey(rawMessage.Type))
                _handlers[rawMessage.Type]?.Invoke(rawMessage);
            else
                    Plugin.Debug($"Unhandled message: {message}");
        }
    }
}
