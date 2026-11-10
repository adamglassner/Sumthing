namespace Sumthing.Engine;

public sealed class Unit
{
    public string Name { get; init; }
    public string Symbol { get; init; }
    /// <summary>How many base units one of this unit is (ignored for temperature).</summary>
    public double Factor { get; init; }
    public override string ToString() => $"{Name} ({Symbol})";
}

public sealed class UnitCategory
{
    public string Name { get; init; }
    public string Icon { get; init; }
    public List<Unit> Units { get; init; } = new();
    public bool IsCurrency { get; init; }
    public bool IsTemperature { get; init; }
    /// <summary>Units picked when the category opens.</summary>
    public int DefaultFrom { get; init; }
    public int DefaultTo { get; init; } = 1;
    public override string ToString() => Name;
}

/// <summary>Unit tables. Every factor is the exact defined value where one exists (inch = 2.54 cm exactly, etc.).</summary>
public static class Units
{
    private static Unit U(string name, string sym, double f) => new() { Name = name, Symbol = sym, Factor = f };

    public static readonly List<UnitCategory> Categories = new()
    {
        new UnitCategory
        {
            Name = "Currency", Icon = "", IsCurrency = true, // units are filled from the rates
        },
        new UnitCategory
        {
            Name = "Length", Icon = "", DefaultFrom = 7, DefaultTo = 3,
            Units =
            {
                U("Millimeters", "mm", 0.001), U("Centimeters", "cm", 0.01), U("Meters", "m", 1), U("Kilometers", "km", 1000),
                U("Inches", "in", 0.0254), U("Feet", "ft", 0.3048), U("Yards", "yd", 0.9144), U("Miles", "mi", 1609.344),
                U("Nautical miles", "nmi", 1852),
            },
        },
        new UnitCategory
        {
            Name = "Weight", Icon = "", DefaultFrom = 5, DefaultTo = 2,
            Units =
            {
                U("Milligrams", "mg", 1e-6), U("Grams", "g", 0.001), U("Kilograms", "kg", 1), U("Metric tonnes", "t", 1000),
                U("Ounces", "oz", 0.028349523125), U("Pounds", "lb", 0.45359237), U("Stone", "st", 6.35029318),
                U("US tons", "ton", 907.18474),
            },
        },
        new UnitCategory
        {
            Name = "Temperature", Icon = "", IsTemperature = true, DefaultFrom = 1, DefaultTo = 0,
            Units = { U("Celsius", "°C", 0), U("Fahrenheit", "°F", 0), U("Kelvin", "K", 0) },
        },
        new UnitCategory
        {
            Name = "Volume", Icon = "", DefaultFrom = 9, DefaultTo = 1,
            Units =
            {
                U("Milliliters", "mL", 0.001), U("Liters", "L", 1), U("Cubic meters", "m³", 1000),
                U("Teaspoons (US)", "tsp", 0.00492892159375), U("Tablespoons (US)", "tbsp", 0.01478676478125),
                U("Fluid ounces (US)", "fl oz", 0.0295735295625), U("Cups (US)", "cup", 0.2365882365),
                U("Pints (US)", "pt", 0.473176473), U("Quarts (US)", "qt", 0.946352946), U("Gallons (US)", "gal", 3.785411784),
                U("Gallons (UK)", "UK gal", 4.54609),
            },
        },
        new UnitCategory
        {
            Name = "Area", Icon = "", DefaultFrom = 5, DefaultTo = 1,
            Units =
            {
                U("Square centimeters", "cm²", 1e-4), U("Square meters", "m²", 1), U("Hectares", "ha", 10000), U("Square kilometers", "km²", 1e6),
                U("Square inches", "in²", 0.00064516), U("Square feet", "ft²", 0.09290304), U("Square yards", "yd²", 0.83612736),
                U("Acres", "ac", 4046.8564224), U("Square miles", "mi²", 2589988.110336),
            },
        },
        new UnitCategory
        {
            Name = "Speed", Icon = "", DefaultFrom = 2, DefaultTo = 1,
            Units =
            {
                U("Meters per second", "m/s", 1), U("Kilometers per hour", "km/h", 1 / 3.6), U("Miles per hour", "mph", 0.44704),
                U("Knots", "kn", 1852.0 / 3600.0), U("Feet per second", "ft/s", 0.3048),
            },
        },
        new UnitCategory
        {
            Name = "Time", Icon = "", DefaultFrom = 3, DefaultTo = 2,
            Units =
            {
                U("Milliseconds", "ms", 0.001), U("Seconds", "s", 1), U("Minutes", "min", 60), U("Hours", "h", 3600),
                U("Days", "d", 86400), U("Weeks", "wk", 604800), U("Months (average)", "mo", 2629746), U("Years (average)", "yr", 31556952),
            },
        },
        new UnitCategory
        {
            Name = "Data", Icon = "", DefaultFrom = 4, DefaultTo = 3,
            Units =
            {
                U("Bits", "bit", 0.125), U("Bytes", "B", 1), U("Kilobytes", "KB", 1e3), U("Megabytes", "MB", 1e6),
                U("Gigabytes", "GB", 1e9), U("Terabytes", "TB", 1e12), U("Kibibytes", "KiB", 1024), U("Mebibytes", "MiB", 1048576),
                U("Gibibytes", "GiB", 1073741824), U("Tebibytes", "TiB", 1099511627776),
            },
        },
        new UnitCategory
        {
            Name = "Fuel economy", Icon = "", DefaultFrom = 0, DefaultTo = 2,
            // handled specially: L/100 km is the inverse of the others
            Units = { U("Miles per gallon (US)", "mpg", 0.425143707), U("Miles per gallon (UK)", "mpg UK", 0.354006189), U("Liters per 100 km", "L/100 km", -1), U("Kilometers per liter", "km/L", 1) },
        },
    };

    /// <summary>Converts <paramref name="value"/> between two units of the same (non-currency) category.</summary>
    public static double Convert(UnitCategory cat, Unit from, Unit to, double value)
    {
        if (from == to) return value;
        if (cat.IsTemperature)
        {
            double c = from.Symbol switch
            {
                "°F" => (value - 32) * 5 / 9,
                "K" => value - 273.15,
                _ => value,
            };
            return to.Symbol switch
            {
                "°F" => c * 9 / 5 + 32,
                "K" => c + 273.15,
                _ => c,
            };
        }
        if (from.Factor < 0 || to.Factor < 0)
        {
            // fuel economy: base is km/L; L/100 km = 100 / (km/L)
            double kmPerL = from.Factor < 0 ? (value == 0 ? double.PositiveInfinity : 100 / value) : value * from.Factor;
            return to.Factor < 0 ? (kmPerL == 0 ? double.PositiveInfinity : 100 / kmPerL) : kmPerL / to.Factor;
        }
        return value * from.Factor / to.Factor;
    }
}
