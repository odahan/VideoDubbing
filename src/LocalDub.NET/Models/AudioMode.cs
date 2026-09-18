namespace LocalDub.Models;

/// <summary>How the dubbed voice is mixed back with the original video's audio.</summary>
public enum AudioMode
{
    /// <summary>Removes the estimated original voice, keeps the accompaniment, and adds the dub.</summary>
    Separate,
    /// <summary>Automatically ducks (attenuates) the original track while the dub plays.</summary>
    Duck,
    /// <summary>Produces a new video with only the English voice; the original mix is kept separately.</summary>
    ExternalMix
}
