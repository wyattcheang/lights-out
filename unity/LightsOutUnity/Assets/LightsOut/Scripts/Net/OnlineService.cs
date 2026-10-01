// Public / private rooms through Unity Multiplayer Services sessions (Relay transport).
// Public sessions are listed with QuerySessionsAsync; private ones are joined with the session code.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

namespace LightsOut
{
    public class OnlineService
    {
        public ISession Session;
        public string Status = "";
        public bool Ready;

        public async Task<bool> Init()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Ready = true; Status = ""; return true;
            }
            catch (Exception e) { Status = "Online unavailable: " + e.Message; return false; }
        }

        public async Task<bool> Host(bool isPrivate, string roomName)
        {
            if (!Ready && !await Init()) return false;
            try
            {
                var options = new SessionOptions { MaxPlayers = 10, IsPrivate = isPrivate, Name = roomName }.WithRelayNetwork();
                Session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Status = "Room code " + Session.Code; return true;
            }
            catch (Exception e) { Status = "Could not create the room: " + e.Message; return false; }
        }

        public async Task<bool> JoinByCode(string code)
        {
            if (!Ready && !await Init()) return false;
            try { Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant()); Status = ""; return true; }
            catch (Exception e) { Status = "Could not join: " + e.Message; return false; }
        }

        public async Task<bool> JoinById(string id)
        {
            if (!Ready && !await Init()) return false;
            try { Session = await MultiplayerService.Instance.JoinSessionByIdAsync(id); Status = ""; return true; }
            catch (Exception e) { Status = "Could not join: " + e.Message; return false; }
        }

        public async Task<List<ISessionInfo>> ListPublic()
        {
            if (!Ready && !await Init()) return new List<ISessionInfo>();
            try { var r = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions()); return new List<ISessionInfo>(r.Sessions); }
            catch (Exception e) { Status = "Could not list rooms: " + e.Message; return new List<ISessionInfo>(); }
        }

        public async Task Leave()
        {
            if (Session == null) return;
            try { await Session.LeaveAsync(); } catch (Exception) { }
            Session = null;
        }
    }
}
