using System.Text.Json.Nodes;
using AniSync.AniList;
using AniSync.Core;

namespace AniSync.Tests;

public class WikidataTitlesTests
{
    const string Search = """
        {"search":[
          {"id":"Q22100530","label":"Moi, quand je me réincarne en Slime","match":{"type":"label","language":"fr","text":"Moi, quand je me réincarne en Slime"}},
          {"id":"Q113841116","label":"Moi, quand je me réincarne en Slime - Le Film : Scarlet Bond","match":{"type":"label","language":"fr","text":"Moi, quand je me réincarne en Slime - Le Film : Scarlet Bond"}},
          {"id":"Q104817254","label":"Moi, quand je me réincarne en slime","match":{"type":"label","language":"fr","text":"Moi, quand je me réincarne en slime"}}
        ]}
        """;

    const string Entities = """
        {"entities":{
          "Q22100530":{"labels":{"ja":{"value":"転生したらスライムだった件"},"en":{"value":"That Time I Got Reincarnated as a Slime"}},
                       "aliases":{"en":[{"value":"Tensei shitara Slime Datta Ken"},{"value":"TenSura"},{"value":"Regarding Reincarnated to Slime"}]}},
          "Q104817254":{"labels":{"ja":{"value":"転生したらスライムだった件"},"en":{"value":"That Time I Got Reincarnated as a Slime"}},"aliases":{}}
        }}
        """;

    static readonly string Norm = TitleParser.Normalize("Moi, quand je me réincarne en Slime");

    [Fact]
    public void Keeps_only_entries_whose_name_really_matches()
    {
        var ids = WikidataTitles.RelevantIds(JsonNode.Parse(Search), Norm).ToList();

        // Le film « ... - Le Film : Scarlet Bond » commence pareil mais n'est pas le même titre.
        Assert.Equal(["Q22100530", "Q104817254"], ids);
    }

    [Fact]
    public void Returns_english_then_romaji_then_japanese_without_duplicates_or_abbreviations()
    {
        var titles = WikidataTitles.ExtractTitles(JsonNode.Parse(Entities), ["Q22100530", "Q104817254"], Norm);

        Assert.Equal(
            ["That Time I Got Reincarnated as a Slime", "Tensei shitara Slime Datta Ken", "Regarding Reincarnated to Slime", "転生したらスライムだった件"],
            titles);
    }

    [Fact]
    public void Unknown_title_gives_nothing()
    {
        Assert.Empty(WikidataTitles.RelevantIds(JsonNode.Parse("""{"search":[]}"""), Norm));
        Assert.Empty(WikidataTitles.ExtractTitles(JsonNode.Parse("""{"entities":{}}"""), [], Norm));
    }
}
