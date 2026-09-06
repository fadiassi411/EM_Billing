"""Render the generated DOCX files to PDF without changing their source content."""
from pathlib import Path
from io import BytesIO
from docx import Document
from docx.table import Table as DocxTable
from docx.text.paragraph import Paragraph as DocxParagraph
from docx.oxml.ns import qn
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, PageBreak, CondPageBreak, Image, Table, TableStyle, KeepTogether
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfbase import pdfmetrics
from xml.sax.saxutils import escape

ROOT=Path(__file__).resolve().parents[1]
NAVY=colors.HexColor('#0B2B3A'); TEAL=colors.HexColor('#008F83'); GREY=colors.HexColor('#526875'); PALE=colors.HexColor('#F2F7F8')
try:
    pdfmetrics.registerFont(TTFont('Aptos','C:/Windows/Fonts/aptos.ttf'))
    pdfmetrics.registerFont(TTFont('AptosBold','C:/Windows/Fonts/aptos-bold.ttf'))
except: pass

def iter_blocks(doc):
    for child in doc.element.body.iterchildren():
        if child.tag==qn('w:p'): yield DocxParagraph(child,doc)
        elif child.tag==qn('w:tbl'): yield DocxTable(child,doc)

def styles():
    base='Aptos' if 'Aptos' in pdfmetrics.getRegisteredFontNames() else 'Helvetica'
    bold='AptosBold' if 'AptosBold' in pdfmetrics.getRegisteredFontNames() else 'Helvetica-Bold'
    s=getSampleStyleSheet()
    return {
      'body':ParagraphStyle('body',fontName=base,fontSize=8.6,leading=11.2,textColor=GREY,spaceAfter=5),
      'bullet':ParagraphStyle('bullet',fontName=base,fontSize=8.4,leading=10.8,textColor=GREY,leftIndent=15,firstLineIndent=-9,bulletIndent=5,spaceAfter=4),
      'title':ParagraphStyle('title',fontName=bold,fontSize=25,leading=29,textColor=NAVY,spaceAfter=10,alignment=TA_CENTER),
      'h1':ParagraphStyle('h1',fontName=bold,fontSize=18,leading=21,textColor=NAVY,spaceBefore=8,spaceAfter=7),
      'h2':ParagraphStyle('h2',fontName=bold,fontSize=12,leading=14,textColor=TEAL,spaceBefore=7,spaceAfter=5),
      'h3':ParagraphStyle('h3',fontName=bold,fontSize=10,leading=12,textColor=NAVY,spaceBefore=5,spaceAfter=4),
      'caption':ParagraphStyle('caption',fontName=base,fontSize=7.3,leading=9,textColor=GREY,alignment=TA_CENTER,spaceAfter=6),
      'subtitle':ParagraphStyle('subtitle',fontName=base,fontSize=11,leading=14,textColor=TEAL,alignment=TA_CENTER,spaceAfter=8),
      'label':ParagraphStyle('label',fontName=bold,fontSize=7,leading=8,textColor=TEAL,spaceAfter=3),
      'cell':ParagraphStyle('cell',fontName=base,fontSize=7.4,leading=9,textColor=GREY),
      'cellh':ParagraphStyle('cellh',fontName=bold,fontSize=7.4,leading=9,textColor=colors.white),
    }

def image_from_paragraph(p,doc):
    blips=p._p.xpath('.//a:blip')
    if not blips: return None
    rid=blips[0].get(qn('r:embed')); part=doc.part.related_parts[rid]
    data=part.blob; from PIL import Image as PILImage
    pil=PILImage.open(BytesIO(data)); w,h=pil.size
    extents=p._p.xpath('.//wp:extent')
    if extents:
        # Preserve the size chosen in the authored Word document (English Metric Units).
        target_w=float(extents[0].get('cx'))/914400*inch
        target_h=float(extents[0].get('cy'))/914400*inch
        scale=min(1.0,(7.0*inch)/target_w,(8.0*inch)/target_h)
        return Image(BytesIO(data),width=target_w*scale,height=target_h*scale)
    maxw=7.0*inch; maxh=8.0*inch; scale=min(maxw/w,maxh/h)
    return Image(BytesIO(data),width=w*scale,height=h*scale)

def paragraph_flow(p,doc,st):
    if p._p.xpath('.//w:br[@w:type="page"]'): return [CondPageBreak(8*inch)]
    pic=image_from_paragraph(p,doc)
    if pic: return [Spacer(1,3),pic,Spacer(1,3)]
    text=p.text.strip()
    if not text: return [Spacer(1,3)]
    name=p.style.name if p.style else ''
    if name=='Title': key='title'
    elif name=='Subtitle': key='subtitle'
    elif name=='Heading 1': key='h1'
    elif name=='Heading 2': key='h2'
    elif name=='Heading 3': key='h3'
    elif name=='Caption': key='caption'
    elif text.isupper() and len(text)<70: key='label'
    elif name.startswith('List Bullet'): return [Paragraph('• '+escape(text),st['bullet'])]
    elif name.startswith('List Number'): return [Paragraph(escape(text),st['bullet'])]
    else: key='body'
    return [Paragraph(escape(text),st[key])]

def table_flow(tbl,st):
    data=[]
    for ri,row in enumerate(tbl.rows):
        data.append([Paragraph(escape(c.text.strip()).replace('\n','<br/>'),st['cellh' if ri==0 else 'cell']) for c in row.cells])
    if not data: return Spacer(1,1)
    t=Table(data,repeatRows=1,hAlign='CENTER')
    cmds=[('GRID',(0,0),(-1,-1),.35,colors.HexColor('#CAD8DC')),('VALIGN',(0,0),(-1,-1),'TOP'),('LEFTPADDING',(0,0),(-1,-1),6),('RIGHTPADDING',(0,0),(-1,-1),6),('TOPPADDING',(0,0),(-1,-1),5),('BOTTOMPADDING',(0,0),(-1,-1),5),('BACKGROUND',(0,0),(-1,0),NAVY)]
    for i in range(1,len(data)):
        if i%2==0: cmds.append(('BACKGROUND',(0,i),(-1,i),PALE))
    t.setStyle(TableStyle(cmds)); return t

def footer(canvas,doc):
    canvas.saveState(); canvas.setStrokeColor(colors.HexColor('#D4E0E3')); canvas.line(.7*inch,.48*inch,A4[0]-.7*inch,.48*inch)
    canvas.setFont('Helvetica',6.5); canvas.setFillColor(GREY); canvas.drawString(.7*inch,.3*inch,'WATCH DOG ENERGY MANAGEMENT  •  CUSTOMER USER GUIDE'); canvas.drawRightString(A4[0]-.7*inch,.3*inch,f'PAGE {doc.page}'); canvas.restoreState()

def convert(docx,pdf):
    doc=Document(docx); st=styles(); story=[]
    for block in iter_blocks(doc):
        if isinstance(block,DocxParagraph): story.extend(paragraph_flow(block,doc,st))
        else: story.extend([table_flow(block,st),Spacer(1,7)])
    out=SimpleDocTemplate(str(pdf),pagesize=A4,rightMargin=.58*inch,leftMargin=.58*inch,topMargin=.55*inch,bottomMargin=.62*inch,title='Watch Dog Energy Management Customer User Guide',author='MicroBrain by Fadi Assi')
    out.build(story,onFirstPage=footer,onLaterPages=footer)

if __name__=='__main__':
    convert(ROOT/'Watch_Dog_EM_Customer_User_Guide.docx',ROOT/'Watch_Dog_EM_Customer_User_Guide.pdf')
    convert(ROOT/'Watch_Dog_EM_Quick_User_Guide.docx',ROOT/'Watch_Dog_EM_Quick_User_Guide.pdf')
    print('PDF_CREATED')
