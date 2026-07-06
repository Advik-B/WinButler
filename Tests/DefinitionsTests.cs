using System.Linq;
using WinButler.Models;
using WinButler.Services.Definitions;
using Xunit;

namespace WinButler.Tests;

public class DefinitionsTests
{
    [Fact]
    public void Bundled_definitions_parse_and_are_populated()
    {
        var defs = BundledDefinitionSource.Load();

        Assert.True(defs.Version >= 1);
        Assert.True(defs.Cache.AlwaysSafeNames.Count > 20);
        Assert.True(defs.Cache.SafeContextFragments.Count > 30);
        Assert.True(defs.Redirect.Entries.Count >= 55);
        Assert.Contains(".ssh", defs.Redirect.DenyNames);
    }

    [Fact]
    public void Bundled_load_folds_every_definitions_file_together()
    {
        // cache.json and redirect.json are separate files; a correct fold has BOTH populated.
        var defs = BundledDefinitionSource.Load();

        Assert.NotEmpty(defs.Cache.DenyFragments);   // from cache.json — the safety-critical list
        Assert.NotEmpty(defs.Redirect.Entries);      // from redirect.json
    }

    [Fact]
    public void Redirect_target_names_are_unique()
    {
        var defs = BundledDefinitionSource.Load();

        var duplicates = defs.Redirect.Entries
            .GroupBy(e => e.TargetName, System.StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Redirect_entries_have_required_fields()
    {
        var defs = BundledDefinitionSource.Load();

        Assert.All(defs.Redirect.Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.RelativeToProfile));
            Assert.False(string.IsNullOrWhiteSpace(e.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(e.TargetName));
        });
    }

    [Fact]
    public void Merge_adds_new_redirect_entry()
    {
        var baseDefs = BundledDefinitionSource.Load();
        var overlay = new WinButlerDefinitions
        {
            Redirect = new RedirectRuleSet
            {
                Entries = { Entry(".mytool", "MyTool", ".mytool") },
            },
        };

        var merged = WinButlerDefinitions.Merge(baseDefs, overlay);

        Assert.Contains(merged.Redirect.Entries, e => e.TargetName == ".mytool");
        Assert.Equal(baseDefs.Redirect.Entries.Count + 1, merged.Redirect.Entries.Count);
    }

    [Fact]
    public void Merge_overrides_existing_entry_by_target_name()
    {
        var baseDefs = BundledDefinitionSource.Load();
        var overlay = new WinButlerDefinitions
        {
            Redirect = new RedirectRuleSet
            {
                Entries = { Entry(".cargo", "Cargo (overridden)", ".cargo") },
            },
        };

        var merged = WinButlerDefinitions.Merge(baseDefs, overlay);

        Assert.Equal("Cargo (overridden)",
            merged.Redirect.Entries.Single(e => e.TargetName == ".cargo").DisplayName);
    }

    [Fact]
    public void Merge_unions_cache_names_without_duplicates()
    {
        var baseDefs = BundledDefinitionSource.Load();
        var overlay = new WinButlerDefinitions
        {
            // "GPUCache" already exists in base; "MyNewToolCache" is new.
            Cache = new CacheRuleSet { AlwaysSafeNames = { "GPUCache", "MyNewToolCache" } },
        };

        var merged = WinButlerDefinitions.Merge(baseDefs, overlay);

        Assert.Contains("MyNewToolCache", merged.Cache.AlwaysSafeNames);
        Assert.Contains("GPUCache", merged.Cache.AlwaysSafeNames);
        Assert.Equal(1, merged.Cache.AlwaysSafeNames.Count(n => n == "GPUCache"));
    }

    [Fact]
    public void Bundled_known_locations_catalog_is_populated_and_well_formed()
    {
        var entries = BundledDefinitionSource.Load().KnownLocations.Entries;

        Assert.True(entries.Count >= 40, $"expected a sizeable catalog, got {entries.Count}");

        var validModes = new[] { "children", "files", "self" };
        var validRisks = new[] { "safe", "caution", "risky" };
        Assert.All(entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Id));
            Assert.False(string.IsNullOrWhiteSpace(e.Path));
            Assert.False(string.IsNullOrWhiteSpace(e.DisplayName));
            Assert.Contains(e.Mode.ToLowerInvariant(), validModes);
            Assert.Contains(e.Risk.ToLowerInvariant(), validRisks);
            // "files" mode is meaningless without a pattern.
            if (e.Mode.Equals("files", System.StringComparison.OrdinalIgnoreCase))
                Assert.False(string.IsNullOrWhiteSpace(e.Pattern));
        });
    }

    [Fact]
    public void Known_location_ids_are_unique()
    {
        var entries = BundledDefinitionSource.Load().KnownLocations.Entries;

        var duplicates = entries
            .GroupBy(e => e.Id, System.StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Merge_replaces_known_location_by_id()
    {
        var baseDefs = BundledDefinitionSource.Load();
        var first = baseDefs.KnownLocations.Entries[0];
        var overlay = new WinButlerDefinitions
        {
            KnownLocations = new KnownLocationRuleSet
            {
                Entries = { new KnownLocationEntry { Id = first.Id, Path = "%TEMP%\\overridden", DisplayName = "Overridden" } },
            },
        };

        var merged = WinButlerDefinitions.Merge(baseDefs, overlay);

        Assert.Equal(baseDefs.KnownLocations.Entries.Count, merged.KnownLocations.Entries.Count); // replaced, not added
        Assert.Equal("Overridden", merged.KnownLocations.Entries.Single(e => e.Id == first.Id).DisplayName);
    }

    [Fact]
    public void Merge_takes_highest_version()
    {
        var baseDefs = new WinButlerDefinitions { Version = 1 };
        var overlay = new WinButlerDefinitions { Version = 5 };

        Assert.Equal(5, WinButlerDefinitions.Merge(baseDefs, overlay).Version);
    }

    [Fact]
    public void Parse_throws_on_malformed_json()
    {
        Assert.ThrowsAny<System.Exception>(() => BundledDefinitionSource.Parse("{ not valid json "));
    }

    [Fact]
    public void Provider_fails_closed_when_the_bundled_load_fails()
    {
        var provider = new DefinitionsProvider(() => null); // simulate a failed load

        Assert.True(provider.LoadFailed);
        // Fail closed: an EMPTY ruleset, so no scanner constructed from it can offer anything.
        Assert.Empty(provider.Current.Cache.DenyFragments);
        Assert.Empty(provider.Current.Cache.AlwaysSafeNames);
        Assert.Empty(provider.Current.Redirect.Entries);
    }

    [Fact]
    public void Provider_loads_normally_when_the_bundled_load_succeeds()
    {
        var provider = new DefinitionsProvider();

        Assert.False(provider.LoadFailed);
        Assert.True(provider.Current.Cache.AlwaysSafeNames.Count > 20);
    }

    private static RedirectEntry Entry(string rel, string name, string target) => new()
    {
        RelativeToProfile = rel,
        DisplayName = name,
        Description = "test",
        TargetName = target,
        Category = "Test",
    };
}
