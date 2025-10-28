using Eggbox.Models;

namespace Eggbox.Services;

/// <summary>
/// Houdt de huidige setupstate van de band bij tijdens het instellen.
/// Wordt als Singleton geregistreerd in MauiProgram.cs.
/// </summary>
public class BandStateService
{
    /// <summary>
    /// De bandleden met naam, kleur, instrumenten, gains, enz.
    /// </summary>
    public List<BandMemberSetup> Members { get; private set; } = new();

    /// <summary>
    /// Geeft het totaal aantal leden.
    /// </summary>
    public int Count => Members.Count;

    /// <summary>
    /// Huidige index of geselecteerd lid.
    /// </summary>
    public int CurrentIndex { get; set; } = 0;

    /// <summary>
    /// Stelt de ledenlijst in, meestal vanuit Band.razor.
    /// </summary>
    public void SetMembers(IEnumerable<BandMemberSetup> members)
    {
        Members = members.ToList();
        CurrentIndex = 0;
    }

    /// <summary>
    /// Geeft een lid op basis van BusIndex of Id.
    /// </summary>
    public BandMemberSetup? GetMemberById(int id)
        => Members.FirstOrDefault(m => m.BusIndex == id);

    /// <summary>
    /// Gaat naar het volgende lid als beschikbaar.
    /// </summary>
    public BandMemberSetup? Next()
    {
        if (CurrentIndex + 1 < Members.Count)
        {
            CurrentIndex++;
            return Members[CurrentIndex];
        }
        return null;
    }

    /// <summary>
    /// Gaat naar het vorige lid als beschikbaar.
    /// </summary>
    public BandMemberSetup? Previous()
    {
        if (CurrentIndex - 1 >= 0)
        {
            CurrentIndex--;
            return Members[CurrentIndex];
        }
        return null;
    }

    /// <summary>
    /// Leegmaken bij herstart of annuleren van setup.
    /// </summary>
    public void Reset()
    {
        Members.Clear();
        CurrentIndex = 0;
    }
}