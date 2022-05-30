namespace Dbase;

public class Version : IComparable
{
    
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((Version)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor);
    }

    public Version(int major, int minor)
    {
        Major = major;
        Minor = minor;
    }
    
    public Version(string versionString)
    {
        if(string.IsNullOrEmpty(versionString))
            throw new Exception("Version string is empty");
        
        var parts = versionString.Split('.');
        if(parts.Length != 2)
            throw new Exception("Version string is invalid");
        
        Major = int.Parse(parts[0]);
        Minor = int.Parse(parts[1]);
    }
    
    public int Major { get; }
    public int Minor { get; }

    public static Version Empty => new (0, 0);
    
#pragma warning disable 8602
    public static bool operator == (Version? left, Version? right)
    {
        if (ReferenceEquals(left, null) && ReferenceEquals(right, null))
            return true;
        
        if (ReferenceEquals(left, null) && !ReferenceEquals(right, null))
            return false;
        
        if (!ReferenceEquals(left, null) && ReferenceEquals(right, null))
            return false;

        return left.Major == right.Major && left.Minor == right.Minor;
    }
    
    public static bool operator != (Version? left, Version? right)
    {
        if (ReferenceEquals(left, null) && ReferenceEquals(right, null))
            return false;
        
        if (ReferenceEquals(left, null) && !ReferenceEquals(right, null))
            return true;
        
        if (!ReferenceEquals(left, null) && ReferenceEquals(right, null))
            return true;

        return left.Major != right.Major || left.Minor != right.Minor;
    }
#pragma warning restore 8602
    
    public static bool operator > (Version a, Version b) => (a.Major * 1000000 + a.Minor) > (b.Major * 1000000 + b.Minor);
    public static bool operator < (Version a, Version b) => (a.Major * 1000000 + a.Minor) < (b.Major * 1000000 + b.Minor);
    
    private bool Equals(Version other)
    {
        return Major == other.Major && Minor == other.Minor;
    }
    
    public override string ToString()
    {
        return $"{Major}.{Minor}";
    }

    public int CompareTo(object? obj) {
        if (ReferenceEquals(obj, null))
            return 0;
        
        var a = this;
        var b = (Version)obj;

        return (a.Major * 1000000 + a.Minor).CompareTo(b.Major * 1000000 + b.Minor);
    }
}
