// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

namespace EscapeFromDuckovCoopMod;

public enum CoopAudioEventKind : byte
{
    TwoD = 0,
    ThreeD = 1
}

public struct CoopAudioEventPayload
{
    public CoopAudioEventKind Kind;
    public string EventName;
    public Vector3 Position;
    public bool HasSwitch;
    public string SwitchName;
    public bool HasSoundKey;
    public string SoundKey;

    public void Write(NetDataWriter writer)
    {
        writer.Put((byte)Kind);
        writer.Put(EventName ?? string.Empty);

        if (Kind == CoopAudioEventKind.ThreeD)
        {
            writer.Put(Position.x);
            writer.Put(Position.y);
            writer.Put(Position.z);
        }

        writer.Put(HasSwitch);
        if (HasSwitch)
        {
            writer.Put(SwitchName ?? string.Empty);
        }

        writer.Put(HasSoundKey);
        if (HasSoundKey)
        {
            writer.Put(SoundKey ?? string.Empty);
        }
    }

    public static CoopAudioEventPayload Read(NetDataReader reader)
    {
        var payload = new CoopAudioEventPayload
        {
            Kind = (CoopAudioEventKind)reader.GetByte(),
            EventName = reader.GetString()
        };

        if (payload.Kind == CoopAudioEventKind.ThreeD)
        {
            payload.Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        }
        else
        {
            payload.Position = Vector3.zero;
        }

        payload.HasSwitch = reader.GetBool();
        payload.SwitchName = payload.HasSwitch ? reader.GetString() : string.Empty;

        payload.HasSoundKey = reader.GetBool();
        payload.SoundKey = payload.HasSoundKey ? reader.GetString() : string.Empty;

        return payload;
    }
}
