using CobolAssistant.Core.Parsing;
using Xunit;

namespace CobolAssistant.Tests;

public class ParserTests
{
    private static readonly string Sample =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "RENTBER.cbl"));

    private readonly ProgramAnalysis _analysis = new CobolParser().Parse(Sample);

    [Fact]
    public void Programmname_wird_erkannt()
    {
        Assert.Equal("RENTBER", _analysis.ProgramId);
    }

    [Fact]
    public void Dateien_mit_Zuordnung_und_Organisation()
    {
        Assert.Equal(2, _analysis.Files.Count);
        var input = _analysis.Files[0];
        Assert.Equal("VERSICHERTE", input.Name);
        Assert.Equal("VERSICH.DAT", input.Assignment);
        Assert.Equal("LINE SEQUENTIAL", input.Organization);
    }

    [Fact]
    public void Datenfelder_mit_PIC_Wert_und_Bereich()
    {
        var nr = _analysis.Fields.Single(f => f.Name == "VERS-NR");
        Assert.Equal("X(12)", nr.Picture);
        Assert.Equal("FILE", nr.Section);

        var rentenwert = _analysis.Fields.Single(f => f.Name == "WS-RENTENWERT");
        Assert.Equal("9(2)V99", rentenwert.Picture);
        Assert.Equal("39.32", rentenwert.Value);
        Assert.Equal("WORKING-STORAGE", rentenwert.Section);

        Assert.True(_analysis.Fields.Single(f => f.Name == "DATEI-ENDE").IsCondition);
        Assert.True(_analysis.Fields.Single(f => f.Name == "VERS-SATZ").IsGroup);
    }

    [Fact]
    public void Absaetze_und_Aufrufe()
    {
        Assert.Equal(
            new[] { "HAUPTPROGRAMM", "INITIALISIEREN", "LESEN", "VERARBEITEN", "BERECHNEN", "SCHREIBEN", "ABSCHLIESSEN" },
            _analysis.Paragraphs.Select(p => p.Name));

        var main = _analysis.Paragraphs[0];
        Assert.Equal(new[] { "INITIALISIEREN", "VERARBEITEN", "ABSCHLIESSEN" }, main.Performs);
    }

    [Fact]
    public void Geaenderte_Felder_pro_Absatz()
    {
        var schreiben = _analysis.Paragraphs.Single(p => p.Name == "SCHREIBEN");
        Assert.Equal(new[] { "ERG-NR", "ERG-RENTE", "WS-ANZAHL" }, schreiben.FieldsWritten);

        var berechnen = _analysis.Paragraphs.Single(p => p.Name == "BERECHNEN");
        Assert.Equal(new[] { "WS-MONATSRENTE" }, berechnen.FieldsWritten);
    }

    [Fact]
    public void Komplexitaet_zaehlt_Entscheidungen_aber_nicht_END_IF()
    {
        // UNTIL (Hauptprogramm), AT END (Lesen), IF (Verarbeiten) => 3 Entscheidungen + 1
        Assert.Equal(4, _analysis.Complexity);
    }

    [Fact]
    public void Sauberes_Programm_ohne_Hinweise()
    {
        Assert.Empty(_analysis.Warnings);
    }

    [Fact]
    public void Freies_Format_mit_GO_TO_und_totem_Absatz()
    {
        const string source = """
            IDENTIFICATION DIVISION.
            PROGRAM-ID. FREI.
            DATA DIVISION.
            WORKING-STORAGE SECTION.
            01 WS-UNBENUTZT PIC 9(3).
            PROCEDURE DIVISION.
            START-PARA.
                DISPLAY 'HALLO' *> Kommentar
                GO TO ENDE-PARA.
            ENDE-PARA.
                STOP RUN.
            """;

        var analysis = new CobolParser().Parse(source);

        Assert.Equal("FREI", analysis.ProgramId);
        Assert.Equal(new[] { "START-PARA", "ENDE-PARA" }, analysis.Paragraphs.Select(p => p.Name));
        Assert.Contains(analysis.Warnings, w => w.Contains("GO TO"));
        Assert.Contains(analysis.Warnings, w => w.Contains("ENDE-PARA"));
        Assert.Contains(analysis.Warnings, w => w.Contains("WS-UNBENUTZT"));
    }

    [Theory]
    [InlineData("S9(5)V99", PictureKind.Numeric, 5, 2, true)]
    [InlineData("9(3)", PictureKind.Numeric, 3, 0, false)]
    [InlineData("9V999", PictureKind.Numeric, 1, 3, false)]
    [InlineData("X(12)", PictureKind.Alphanumeric, 0, 0, false)]
    [InlineData("A(5)", PictureKind.Alphabetic, 0, 0, false)]
    public void PIC_Klauseln_werden_ausgewertet(string pic, PictureKind kind, int integer, int decimals, bool signed)
    {
        var parsed = PictureClause.TryParse(pic)!;
        Assert.Equal(kind, parsed.Kind);
        Assert.Equal(integer, parsed.IntegerDigits);
        Assert.Equal(decimals, parsed.DecimalDigits);
        Assert.Equal(signed, parsed.Signed);
    }

    [Fact]
    public void Maximalwert_einer_numerischen_PIC()
    {
        Assert.Equal(999.99m, PictureClause.TryParse("9(3)V99")!.MaxValue);
        Assert.Equal(12, PictureClause.TryParse("X(12)")!.Length);
    }
}
