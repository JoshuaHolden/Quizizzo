using System.Security.Cryptography;
using System.Text;

namespace ClickBaitThumbnailGenerator;

public interface IPromptBuilder
{
    string Build(Scenario scenario);
}

public sealed class PromptBuilder : IPromptBuilder
{
    private const string CorePrompt = """
        Create one original landscape comedy internet-video thumbnail image.

        SCENE:
        {{SCENARIO}}

        The picture should have the visual energy of an exaggerated viral video
        thumbnail: an immediately understandable situation, one strong focal subject,
        a surprising secondary element, expressive fictional characters, dramatic
        lighting, saturated colours, high contrast and a clean bold composition that
        remains readable at a small size.

        The image should invite several different funny interpretations rather than
        communicating one exact title.

        Use two to four important visual elements only. Keep important faces and objects
        within the central 16:9 safe area because the generated image will be cropped.

        Vary the composition naturally. Depending on the supplied scenario, it may use
        a close reaction shot, mysterious discovery, ridiculous scale difference,
        chaotic action, apparent disaster, before-and-after split, strange experiment
        or inexplicable object.

        Do not add writing of any kind.

        ABSOLUTELY NO:
        text, captions, titles, letters, words, numbers, subtitles, watermarks, logos,
        channel names, interface elements, YouTube branding, product branding,
        recognisable trademarks, famous people, influencers, politicians, copyrighted
        characters, graphic injury, sexual material or frightening imagery involving
        children.

        Use original fictional adults and original settings only.

        Produce the image itself, not a screenshot of a webpage and not a thumbnail
        inside another frame.
        """;

    public string Build(Scenario scenario)
    {
        var selector = SHA256.HashData(Encoding.UTF8.GetBytes(scenario.Id));
        var style = selector[0] switch
        {
            < 166 => "Use a colourful photographic or cinematic look.",
            < 204 => "Use a deliberately low-budget homemade-video look, while keeping the image clear.",
            < 230 => "Use a polished original 3D-cartoon look.",
            _ => "Use a documentary or security-camera-inspired look with cinematic clarity."
        };
        var accent = selector[1] < 31
            ? "An occasional non-text visual accent such as one red arrow, circle, or split-screen is allowed only if it clarifies the scene."
            : "Do not use arrows, circles, or a split-screen composition for this image.";

        return CorePrompt.Replace("{{SCENARIO}}", scenario.Scene, StringComparison.Ordinal)
            + Environment.NewLine + Environment.NewLine + style + Environment.NewLine + accent;
    }
}
