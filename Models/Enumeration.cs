using Optional;
using Optional.Collections;
using Blazorise.Extensions;

namespace Eggbox.Models;

public class IntMappedEnumeration<T> : MappedEnumeration<T, int>
where T : IntMappedEnumeration<T>
{
    protected IntMappedEnumeration(string name, int index)
        : base(name, index) { }

    public static explicit operator int(IntMappedEnumeration<T> obj)
        => obj._mappedValue;

    public static Option<T> FromMappedValue(int? mappedValue)
        => mappedValue.ToOption().FlatMap(v => CreateFromMappedValue(v));
    
}

public class StringMappedEnumeration<T> : MappedEnumeration<T, string>
where T : StringMappedEnumeration<T>
{
    protected StringMappedEnumeration(string name, string mappedValue)
        : base(name, mappedValue) { }
}

public abstract class MappedEnumeration<T, TMapped> : Enumeration<T>
where T : MappedEnumeration<T, TMapped>
{
    private static IDictionary<TMapped, Enumeration<T>> _mapLookup = new Dictionary<TMapped, Enumeration<T>>();
    protected readonly TMapped _mappedValue;

    protected MappedEnumeration(string name, TMapped mappedValue)
        : base(name)
    {
        _mappedValue = mappedValue;
        _mapLookup.Add(_mappedValue, this);
    }

    public TMapped MappedValue
        => _mappedValue;

    public static Option<T> FromMappedValue(TMapped mappedValue)
        => CreateFromMappedValue(mappedValue);

    protected static Option<T> CreateFromMappedValue(TMapped mappedValue)
    {
        EnsureStaticFieldsAreInitialized();

        return mappedValue.SomeNotNull()
            .Filter(key => _mapLookup.ContainsKey(key))
            .Map(key => (T)_mapLookup[key]);
    }
}

public abstract class Enumeration
{
}

public class Enumeration<T> : Enumeration, IComparable
    where T : Enumeration<T>
{
    private static IDictionary<string, Enumeration<T>> _nameLookup = new Dictionary<string, Enumeration<T>>();

    protected readonly string _name;
    protected readonly int _sortOrder;
    private static int _highestSortOrder = 0;

    protected Enumeration() { }

    protected Enumeration(string name)
    {
        _name = name;
        _nameLookup.Add(_name, this);
        _sortOrder = _highestSortOrder++;
    }

    public object GetObjectForJson()
        => _name;

    public override string ToString()
        => _name;
    
    public static explicit operator string(Enumeration<T> obj)
        => obj._name;

    public static Option<T> Create(string name)
    {
        EnsureStaticFieldsAreInitialized();

        return name.SomeNotNull()
            .Map(key => key)
            .Filter(key => _nameLookup.ContainsKey(key))
            .Map(key => (T)_nameLookup[key]);
    }

    protected static void EnsureStaticFieldsAreInitialized()
    {
        // The initialization of the static fields of a class 
        // can be postponed until the first static field of that class gets used.
        // Since the values of the static fields of our concrete Enumeration classes 
        // register themselves in a lookuplist when constructed,
        // lookup (and thus Create) fails when these fields have not been instantiated yet.
        // Currently, we use reflection to work around this.

        try
        {
            if (_nameLookup.IsNullOrEmpty())
                typeof(T)
                    .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrNone()
                    .Map(f => f.GetValue(null));
        }
        catch (InvalidOperationException)
        {
            // If another use of a field of this static class has in the meanwhile made the static fields available,
            // the above reflection will fail with message
            //   "Collection was modified; enumeration operation may not execute."
            // This means that the fields are initialized and the purpose of this method is still being accomplished.
        }
    }

#if (DEBUG)
    private static bool _EnsureAllCasesAreHandledInMatchHasRan = false;
#endif
    
    public override bool Equals(object obj)
        => obj is Enumeration<T> enumeration
            ? _name == enumeration._name
            : false;

    public override int GetHashCode() => _name.GetHashCode();

    public int CompareTo(object obj)
    {
        if (obj is Enumeration<T> enumeration)
            return obj == null
                ? 1
                : _sortOrder.CompareTo(enumeration._sortOrder);
        else
            throw new ArgumentException($"obj of type \"{obj.GetType().Name}\" can not be compared to an instance of type \"{nameof(Enumeration<T>)}\"");
    }

    private static IEnumerable<T> _enumerators;
    public static IEnumerable<T> Enumerators
    {
        get
        {
            if (_enumerators.IsNullOrEmpty())
            {
                EnsureStaticFieldsAreInitialized();

                _enumerators = _nameLookup
                    .OrderBy(v => v.Value._sortOrder)
                    .Select(v => (T)v.Value);
            }
            return _enumerators;
        }
    }
}

