using MallEnergyBilling.Web.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace MallEnergyBilling.Web.Services;

public sealed class InvoicePdfService
{
    private static readonly object FontLock = new();
    public InvoicePdfService()
    {
        lock (FontLock)
            if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new WindowsArialFontResolver();
    }

    public byte[] Generate(Invoice invoice)
    {
        var doc = BuildDocument(invoice);
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static Document BuildDocument(Invoice i)
    {
        var teal = Color.Parse("#0C8C84"); var navy = Color.Parse("#0C2637"); var pale = Color.Parse("#EAF5F4"); var gray = Color.Parse("#667781");
        var d = new Document(); d.Info.Title = $"Watch Dog EM Invoice {i.InvoiceNumber}"; d.Info.Author = "Watch Dog EM";
        var normal = d.Styles[StyleNames.Normal]; normal.Font.Name = "Arial"; normal.Font.Size = 9; normal.Font.Color = navy;
        var s = d.AddSection(); s.PageSetup.PageFormat = PageFormat.A4; s.PageSetup.TopMargin = Unit.FromMillimeter(16); s.PageSetup.BottomMargin = Unit.FromMillimeter(18); s.PageSetup.LeftMargin = Unit.FromMillimeter(17); s.PageSetup.RightMargin = Unit.FromMillimeter(17);
        var footer = s.Footers.Primary.AddTable(); footer.AddColumn(Unit.FromMillimeter(115)); footer.AddColumn(Unit.FromMillimeter(45)); var fr=footer.AddRow();fr.Cells[0].AddParagraph("Watch Dog EM  |  Watch Every Watt").Format.Font.Color=gray;var fp=fr.Cells[1].AddParagraph();fp.Format.Alignment=ParagraphAlignment.Right;fp.AddText("Page ");fp.AddPageField();fp.AddText(" of ");fp.AddNumPagesField();

        var head=s.AddTable();head.AddColumn(Unit.FromMillimeter(105));head.AddColumn(Unit.FromMillimeter(55));var hr=head.AddRow();var brand=hr.Cells[0].AddParagraph();var mark=brand.AddFormattedText("WATCH DOG EM",TextFormat.Bold);mark.Font.Size=19;mark.Font.Color=teal;brand.AddLineBreak();var sub=brand.AddFormattedText("WATCH EVERY WATT",TextFormat.Bold);sub.Font.Size=8;sub.Font.Color=gray;var inv=hr.Cells[1];inv.Shading.Color=navy;inv.Format.LeftIndent=Unit.FromMillimeter(5);inv.VerticalAlignment=VerticalAlignment.Center;var ip=inv.AddParagraph("ENERGY INVOICE");ip.Format.Font.Bold=true;ip.Format.Font.Size=16;ip.Format.Font.Color=Colors.White;var ino=inv.AddParagraph(i.InvoiceNumber);ino.Format.Font.Color=Colors.White;ino.Format.SpaceBefore=Unit.FromMillimeter(2);
        s.AddParagraph().Format.SpaceAfter=Unit.FromMillimeter(3);

        var meta=s.AddTable();meta.Borders.Color=Color.Parse("#D9E3E6");meta.Borders.Width=.5;meta.AddColumn(Unit.FromMillimeter(80));meta.AddColumn(Unit.FromMillimeter(80));var mr=meta.AddRow();AddInfo(mr.Cells[0],"BILLED TO",i.Shop?.Name??"Shop",$"Shop {i.Shop?.ShopNumber}\nTenant: {i.Shop?.TenantName}");AddInfo(mr.Cells[1],"INVOICE DETAILS",$"Status: {i.Status}",$"Invoice date: {i.InvoiceDate:dd MMM yyyy}\nDue date: {i.DueDate:dd MMM yyyy}");
        s.AddParagraph().Format.SpaceAfter=Unit.FromMillimeter(4);

        var reading=s.AddTable();reading.Borders.Color=Color.Parse("#D9E3E6");reading.Borders.Width=.5;reading.AddColumn(Unit.FromMillimeter(40));reading.AddColumn(Unit.FromMillimeter(40));reading.AddColumn(Unit.FromMillimeter(40));reading.AddColumn(Unit.FromMillimeter(40));var rh=reading.AddRow();rh.Shading.Color=pale;Header(rh,"METER","PREVIOUS READING","CURRENT READING","CONSUMPTION");var rv=reading.AddRow();Cell(rv.Cells[0],$"{i.Meter?.Name}\n{i.Meter?.SerialNumber}",true);Cell(rv.Cells[1],$"{i.OpeningReading:N2} kWh");Cell(rv.Cells[2],$"{i.ClosingReading:N2} kWh");Cell(rv.Cells[3],$"{i.ConsumptionKwh:N2} kWh",true);
        s.AddParagraph().Format.SpaceAfter=Unit.FromMillimeter(5);

        var bill=s.AddTable();bill.Borders.Color=Color.Parse("#D9E3E6");bill.Borders.Width=.5;bill.AddColumn(Unit.FromMillimeter(85));bill.AddColumn(Unit.FromMillimeter(35));bill.AddColumn(Unit.FromMillimeter(40));var bh=bill.AddRow();bh.Shading.Color=navy;HeaderWhite(bh,"DESCRIPTION","RATE / QTY","AMOUNT");AddLine(bill,"Energy consumption",$"{i.TariffPerKwh:N4} / kWh",i.EnergyCharge,i.Currency);AddLine(bill,"Fixed and service charges","",i.FixedCharges+i.OtherCharges,i.Currency);AddLine(bill,"Tax / VAT","",i.Tax,i.Currency);AddLine(bill,"Discount","",-i.Discount,i.Currency);AddLine(bill,"Previous balance","",i.PreviousBalance,i.Currency);
        s.AddParagraph().Format.SpaceAfter=Unit.FromMillimeter(5);

        var total=s.AddTable();total.AddColumn(Unit.FromMillimeter(95));total.AddColumn(Unit.FromMillimeter(65));var tr=total.AddRow();tr.Cells[1].Shading.Color=teal;var tp=tr.Cells[1].AddParagraph("TOTAL AMOUNT DUE");tp.Format.Font.Bold=true;tp.Format.Font.Color=Colors.White;tp.Format.Alignment=ParagraphAlignment.Center;var tv=tr.Cells[1].AddParagraph($"{i.Currency} {i.Total:N2}");tv.Format.Font.Bold=true;tv.Format.Font.Size=20;tv.Format.Font.Color=Colors.White;tv.Format.Alignment=ParagraphAlignment.Center;tv.Format.SpaceBefore=Unit.FromMillimeter(2);tv.Format.SpaceAfter=Unit.FromMillimeter(2);
        var pay=s.AddParagraph();pay.Format.SpaceBefore=Unit.FromMillimeter(9);var title=pay.AddFormattedText("PAYMENT INSTRUCTIONS",TextFormat.Bold);title.Font.Color=teal;pay.AddLineBreak();pay.AddText("Please quote the invoice number with your payment. Contact mall management for approved payment methods and receipt issuance.");
        var notes=s.AddParagraph();notes.Format.SpaceBefore=Unit.FromMillimeter(6);var nt=notes.AddFormattedText("NOTES & TERMS",TextFormat.Bold);nt.Font.Color=teal;notes.AddLineBreak();notes.AddText(string.IsNullOrWhiteSpace(i.Notes)?"Payment is due by the stated due date. Questions about readings must be raised with mall management before payment.":i.Notes);
        var sign=s.AddTable();sign.Format.SpaceBefore=Unit.FromMillimeter(15);sign.AddColumn(Unit.FromMillimeter(70));sign.AddColumn(Unit.FromMillimeter(20));sign.AddColumn(Unit.FromMillimeter(70));var sr=sign.AddRow();sr.Cells[0].Borders.Top.Width=.7;sr.Cells[0].AddParagraph("Prepared by").Format.Font.Color=gray;sr.Cells[2].Borders.Top.Width=.7;sr.Cells[2].AddParagraph("Authorized signature").Format.Font.Color=gray;
        return d;
    }
    static void AddInfo(Cell c,string title,string line1,string line2){c.Format.LeftIndent=Unit.FromMillimeter(4);c.Format.RightIndent=Unit.FromMillimeter(4);var p=c.AddParagraph(title);p.Format.Font.Bold=true;p.Format.Font.Size=8;p.Format.Font.Color=Color.Parse("#0C8C84");p.Format.SpaceBefore=Unit.FromMillimeter(3);var x=c.AddParagraph(line1);x.Format.Font.Bold=true;x.Format.Font.Size=11;var y=c.AddParagraph(line2);y.Format.SpaceAfter=Unit.FromMillimeter(3);}
    static void Header(Row r,params string[] text){for(int x=0;x<text.Length;x++){var p=r.Cells[x].AddParagraph(text[x]);p.Format.Font.Bold=true;p.Format.Font.Size=7;p.Format.Font.Color=Color.Parse("#667781");}}
    static void HeaderWhite(Row r,params string[] text){for(int x=0;x<text.Length;x++){var p=r.Cells[x].AddParagraph(text[x]);p.Format.Font.Bold=true;p.Format.Font.Size=8;p.Format.Font.Color=Colors.White;if(x>0)p.Format.Alignment=ParagraphAlignment.Right;}}
    static void Cell(Cell c,string text,bool bold=false){var p=c.AddParagraph(text);p.Format.Font.Bold=bold;p.Format.SpaceBefore=Unit.FromMillimeter(2);p.Format.SpaceAfter=Unit.FromMillimeter(2);}
    static void AddLine(Table t,string description,string rate,decimal amount,string currency){var r=t.AddRow();Cell(r.Cells[0],description);var rp=r.Cells[1].AddParagraph(rate);rp.Format.Alignment=ParagraphAlignment.Right;var ap=r.Cells[2].AddParagraph($"{currency} {amount:N2}");ap.Format.Alignment=ParagraphAlignment.Right;ap.Format.Font.Bold=true;}
}

file sealed class WindowsArialFontResolver : IFontResolver
{
    public byte[] GetFont(string faceName)=>File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),faceName));
    public FontResolverInfo ResolveTypeface(string familyName,bool isBold,bool isItalic)=>new(isBold?(isItalic?"arialbi.ttf":"arialbd.ttf"):(isItalic?"ariali.ttf":"arial.ttf"));
}
