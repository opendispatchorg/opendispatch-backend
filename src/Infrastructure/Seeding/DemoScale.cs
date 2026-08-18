namespace OpenDispatch.Infrastructure.Seeding;

/// <summary>
/// How much shop to seed.
/// </summary>
/// <remarks>
/// Two, and the second exists because the first cannot answer a question a deployment has to ask.
/// A demonstration wants one legible day; a measurement wants a database that has been used, because
/// every read path in this system is fast over forty jobs and the ones that are not fast over twelve
/// thousand are exactly what needs finding before a shop finds them.
/// </remarks>
public enum DemoScale
{
    /// <summary>One day, hand-written, legible on a screen — what <c>make seed</c> loads.</summary>
    Demo = 0,

    /// <summary>
    /// The demo day, with a year of finished business behind it. See <see cref="ShopHistory"/>.
    /// </summary>
    Big = 1,
}
