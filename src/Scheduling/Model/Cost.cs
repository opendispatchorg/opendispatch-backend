namespace OpenDispatch.Scheduling.Model;

/// <summary>
/// What a schedule costs, split into the four things that make it expensive.
/// </summary>
/// <remarks>
/// <para>
/// Every term is already multiplied by its weight from <see cref="Objective"/>, so they are in
/// the same currency and <see cref="Total"/> is simply their sum. Keeping them apart rather
/// than collapsing to one number costs nothing and answers the question a dispatcher actually
/// asks of an expensive day — <em>expensive how?</em> — as well as telling a failing test
/// which term is wrong.
/// </para>
/// <para>
/// A <see cref="Lateness"/> above zero is not a fault. A schedule that runs past a promised
/// window is legal and priced; that it shows up here as a number rather than as a refusal is
/// the whole of the soft-constraint design.
/// </para>
/// </remarks>
/// <param name="Travel">Weighted cost of every minute behind the wheel, including the drive home.</param>
/// <param name="Lateness">Weighted cost of every minute a job started past the end of its promised window.</param>
/// <param name="Overtime">Weighted cost of every minute a technician is still out past the end of their shift.</param>
/// <param name="Unassigned">Weighted cost of the jobs nobody could take, scaled by how badly each was needed.</param>
public readonly record struct Cost(double Travel, double Lateness, double Overtime, double Unassigned)
{
    /// <summary>A schedule that costs nothing: the empty day.</summary>
    public static Cost Zero => default;

    /// <summary>What the schedule costs all in — the number the search minimises.</summary>
    public double Total => Travel + Lateness + Overtime + Unassigned;

    /// <summary>The cost of two parts of a schedule taken together, term by term.</summary>
    /// <remarks>
    /// There is deliberately no counterpart that subtracts. Re-pricing a move by taking the
    /// old cost of a route out of a running total and putting the new one in would drift away
    /// from the truth over a long search, by an arithmetic no test would ever catch. The
    /// search adds a schedule up from its parts each time instead.
    /// </remarks>
    public Cost Plus(Cost other) => new(
        Travel + other.Travel,
        Lateness + other.Lateness,
        Overtime + other.Overtime,
        Unassigned + other.Unassigned);
}
