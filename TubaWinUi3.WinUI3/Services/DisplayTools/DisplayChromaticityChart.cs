using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace TubaWinUi3.Services.DisplayTools;

internal static class DisplayChromaticityChart
{
    // CIE 1931 2° spectral locus, sampled at 10 nm (390–700 nm).
    private static readonly Chromaticity[] Locus = [new(.1738,.0049),new(.1733,.0048),new(.1726,.0048),new(.1714,.0051),new(.1689,.0069),new(.1644,.0109),new(.1566,.0177),new(.1440,.0297),new(.1241,.0578),new(.0913,.1327),new(.0454,.2950),new(.0082,.5384),new(.0139,.7502),new(.0743,.8338),new(.1547,.8059),new(.2296,.7543),new(.3016,.6923),new(.3731,.6245),new(.4441,.5547),new(.5125,.4866),new(.5752,.4242),new(.6270,.3725),new(.6658,.3340),new(.6915,.3083),new(.7079,.2920),new(.7190,.2809),new(.7260,.2740),new(.7300,.2700),new(.7320,.2680),new(.7334,.2666),new(.7340,.2660),new(.7347,.2653)];
    private static Point Map(Chromaticity p) => new(40+p.X/.8*400,420-p.Y/.9*400);
    private static readonly Lazy<byte[]> Pixels = new(BuildPixels);
    private static byte[] BuildPixels()
    {
        var b=new byte[400*400*4];
        for(int y=0;y<400;y++) for(int x=0;x<400;x++)
        {
            double cx=x/400.0*.8, cy=(1-y/400.0)*.9;
            bool inside=false;
            for(int i=0,j=Locus.Length-1;i<Locus.Length;j=i++)
                if((Locus[i].Y>cy)!=(Locus[j].Y>cy) && cx<(Locus[j].X-Locus[i].X)*(cy-Locus[i].Y)/(Locus[j].Y-Locus[i].Y)+Locus[i].X) inside=!inside;
            if(!inside || cy<=0) continue;
            double X=cx/cy,Z=(1-cx-cy)/cy;
            double r=3.2406*X-1.5372-.4986*Z, g=-.9689*X+1.8758+.0415*Z, blue=.0557*X-.204+1.057*Z;
            double max=Math.Max(1,Math.Max(r,Math.Max(g,blue)));
            byte Gamma(double v) { v=Math.Clamp(v/max,0,1); return (byte)(255*(v<=.0031308?12.92*v:1.055*Math.Pow(v,1/2.4)-.055)); }
            int p=(y*400+x)*4; b[p]=Gamma(blue); b[p+1]=Gamma(g); b[p+2]=Gamma(r); b[p+3]=255;
        }
        return b;
    }
    public static void Draw(Canvas canvas,Chromaticity[] primaries,Chromaticity? white)
    {
        canvas.Children.Clear();
        var bitmap=new WriteableBitmap(400,400);
        using(var s=bitmap.PixelBuffer.AsStream()) s.Write(Pixels.Value);
        bitmap.Invalidate();
        var image=new Image { Source=bitmap,Width=400,Height=400 }; Canvas.SetLeft(image,40); Canvas.SetTop(image,20); canvas.Children.Add(image);
        var gridBrush=new SolidColorBrush(canvas.ActualTheme==ElementTheme.Dark?Colors.White:Colors.Black) { Opacity=.2 };
        for(int i=0;i<=9;i++)
        {
            double y=420-i/9.0*400;
            canvas.Children.Add(new Line { X1=40,X2=440,Y1=y,Y2=y,Stroke=gridBrush,StrokeThickness=.5 });
            var t=new TextBlock { Text=(i/10.0).ToString("F1") }; Canvas.SetLeft(t,8);Canvas.SetTop(t,y-10);canvas.Children.Add(t);
        }
        for(int i=0;i<=8;i++)
        {
            double x=40+i/8.0*400;
            canvas.Children.Add(new Line { X1=x,X2=x,Y1=20,Y2=420,Stroke=gridBrush,StrokeThickness=.5 });
            var t=new TextBlock { Text=(i/10.0).ToString("F1") };Canvas.SetLeft(t,x-8);Canvas.SetTop(t,428);canvas.Children.Add(t);
        }
        void Triangle(Chromaticity[] points,Color color,bool dashed)
        {
            var poly=new Polygon { Stroke=new SolidColorBrush(color),StrokeThickness=dashed?1.5:3 };
            foreach(var p in points) poly.Points.Add(Map(p));
            if(dashed) poly.StrokeDashArray=new DoubleCollection { 4,3 };
            canvas.Children.Add(poly);
        }
        Triangle(DisplayGamut.Standards[0].Points,Colors.White,true);
        Triangle(DisplayGamut.Standards[1].Points,Colors.Yellow,true);
        Triangle(DisplayGamut.Standards[3].Points,Colors.Cyan,true);
        if(DisplayGamut.IsValid(primaries)) Triangle(primaries,Colors.DeepPink,false);
        if(white is {} w && w.X>0 && w.Y>0)
        {
            var point=Map(w); var mark=new Ellipse { Width=8,Height=8,Fill=new SolidColorBrush(Colors.White),Stroke=new SolidColorBrush(Colors.Black),StrokeThickness=1 };
            Canvas.SetLeft(mark,point.X-4);Canvas.SetTop(mark,point.Y-4);canvas.Children.Add(mark);
        }
    }
}
