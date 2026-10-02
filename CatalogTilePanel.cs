using System.Windows;
using System.Windows.Controls;

namespace PocketDeadlock;

// The catalog is paged (24 entries), so a small reflowing panel needs no virtualized index.
public sealed class CatalogTilePanel : Panel
{
    const double Gap=12;
    public int Columns {get;private set;}=1;
    double tileWidth,rowHeight;
    internal static (int Columns,double TileWidth) Layout(double width)
    {
        width=double.IsFinite(width)?Math.Max(0,width):680;
        int columns=Math.Max(1,(int)Math.Floor((width+Gap)/(220+Gap)));
        return (columns,Math.Max(0,(width-Gap*(columns-1))/columns));
    }
    protected override Size MeasureOverride(Size available)
    {
        (Columns,tileWidth)=Layout(available.Width);rowHeight=0;
        foreach(UIElement child in InternalChildren) {child.Measure(new Size(tileWidth,double.PositiveInfinity));rowHeight=Math.Max(rowHeight,child.DesiredSize.Height);}
        int rows=(InternalChildren.Count+Columns-1)/Columns;
        return new Size(tileWidth*Columns+Gap*(Columns-1),rows==0?0:rows*rowHeight+(rows-1)*Gap);
    }
    protected override Size ArrangeOverride(Size final)
    {
        for(int i=0;i<InternalChildren.Count;i++) InternalChildren[i].Arrange(new Rect(i%Columns*(tileWidth+Gap),i/Columns*(rowHeight+Gap),tileWidth,rowHeight));
        return final;
    }
}
