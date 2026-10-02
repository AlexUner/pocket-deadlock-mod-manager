using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PocketDeadlock;

// Pixel scrolling with two rows of overscan: long indexes do not create long visual trees.
public sealed class CatalogTilePanel : VirtualizingPanel, IScrollInfo
{
    const double Gap=12;
    public int Columns {get;private set;}=1;
    public double RowStride=>rowHeight+Gap;
    double tileWidth,rowHeight=240;
    Size extent,viewport;
    Point offset;
    public ScrollViewer? ScrollOwner {get;set;}
    public bool CanVerticallyScroll {get;set;}
    public bool CanHorizontallyScroll {get;set;}
    public double ExtentWidth=>extent.Width;
    public double ExtentHeight=>extent.Height;
    public double ViewportWidth=>viewport.Width;
    public double ViewportHeight=>viewport.Height;
    public double HorizontalOffset=>0;
    public double VerticalOffset=>offset.Y;
    internal static (int Columns,double TileWidth) Layout(double width)
    {
        width=double.IsFinite(width)?Math.Max(0,width):680;
        int columns=Math.Max(1,(int)Math.Floor((width+Gap)/(220+Gap)));
        return (columns,Math.Max(0,(width-Gap*(columns-1))/columns));
    }
    protected override Size MeasureOverride(Size available)
    {
        var owner=ItemsControl.GetItemsOwner(this);
        if(owner==null) return default;
        double width=double.IsFinite(available.Width)?available.Width:Math.Max(1,owner.ActualWidth);
        double height=double.IsFinite(available.Height)?available.Height:Math.Max(1,owner.ActualHeight);
        viewport=new Size(width,height);(Columns,tileWidth)=Layout(width);
        double cover=Math.Min(Math.Max(0,tileWidth-18)*9/16,Math.Max(72,owner.ActualHeight-120));
        rowHeight=cover+116;UpdateExtent(owner.Items.Count);
        var (first,last)=VisibleRange(owner.Items.Count,Columns,RowStride,offset.Y,height);
        _=InternalChildren.Count; // Initialize the items host before asking WPF for its generator.
        var generator=ItemContainerGenerator;
        if(generator==null) return viewport;
        var position=generator.GeneratorPositionFromIndex(first);
        int childIndex=position.Offset==0?position.Index:position.Index+1;
        if(last>=first)
        {
            using(generator.StartAt(position,GeneratorDirection.Forward,true))
                for(int index=first;index<=last;index++,childIndex++)
                {
                    var child=(UIElement)generator.GenerateNext(out bool fresh);
                    if(fresh)
                    {
                        if(childIndex>=InternalChildren.Count) AddInternalChild(child);else InsertInternalChild(childIndex,child);
                        generator.PrepareItemContainer(child);
                    }
                    child.Measure(new Size(tileWidth,double.PositiveInfinity));
                    rowHeight=child.DesiredSize.Height;
                }
        }
        // Remove distant containers, including their pending image requests.
        for(int i=InternalChildren.Count-1;i>=0;i--)
        {
            var childPosition=new GeneratorPosition(i,0);
            int index=generator.IndexFromGeneratorPosition(childPosition);
            if(index<first || index>last) {generator.Remove(childPosition,1);RemoveInternalChildRange(i,1);}
        }
        UpdateExtent(owner.Items.Count);ScrollOwner?.InvalidateScrollInfo();
        return viewport;
    }
    internal static (int First,int Last) VisibleRange(int count,int columns,double stride,double y,double height)
    {
        if(count==0) return (0,-1);
        int firstRow=Math.Max(0,(int)Math.Floor(y/stride)-2);
        int lastRow=(int)Math.Floor((y+height)/stride)+2;
        return (Math.Min(count-1,firstRow*columns),Math.Min(count-1,(lastRow+1)*columns-1));
    }
    void UpdateExtent(int count)
    {
        int rows=(count+Columns-1)/Columns;
        extent=new Size(viewport.Width,Math.Max(0,rows*RowStride-Gap));
        offset.Y=Math.Clamp(offset.Y,0,Math.Max(0,extent.Height-viewport.Height));
    }
    protected override Size ArrangeOverride(Size final)
    {
        for(int i=0;i<InternalChildren.Count;i++)
        {
            int index=ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i,0));
            InternalChildren[i].Arrange(new Rect(index%Columns*(tileWidth+Gap),index/Columns*RowStride-offset.Y,tileWidth,rowHeight));
        }
        return final;
    }
    protected override void OnItemsChanged(object sender,ItemsChangedEventArgs e)
    {
        if(e.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Reset) RemoveInternalChildRange(0,InternalChildren.Count);
        else if(e.ItemUICount>0 && e.Position.Index>=0 && e.Position.Index<InternalChildren.Count)
            RemoveInternalChildRange(e.Position.Index,Math.Min(e.ItemUICount,InternalChildren.Count-e.Position.Index));
        InvalidateMeasure();
    }
    protected override void BringIndexIntoView(int index)
    {
        double top=index/Columns*RowStride,bottom=top+rowHeight;
        if(top<offset.Y) SetVerticalOffset(top);else if(bottom>offset.Y+viewport.Height) SetVerticalOffset(bottom-viewport.Height);
    }
    public Rect MakeVisible(Visual visual,Rect rectangle)
    {
        DependencyObject? child=visual;
        while(child!=null && VisualTreeHelper.GetParent(child)!=this) child=VisualTreeHelper.GetParent(child);
        if(child is UIElement element)
        {
            int index=InternalChildren.IndexOf(element);
            if(index>=0) BringIndexIntoView(ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(index,0)));
        }
        return rectangle;
    }
    public void SetVerticalOffset(double value)
    {
        if(double.IsNaN(value)) return;
        value=Math.Clamp(value,0,Math.Max(0,extent.Height-viewport.Height));
        if(Math.Abs(value-offset.Y)<.01) return;
        offset.Y=value;InvalidateMeasure();ScrollOwner?.InvalidateScrollInfo();
    }
    public void SetHorizontalOffset(double value) { }
    public void LineUp()=>SetVerticalOffset(offset.Y-48);
    public void LineDown()=>SetVerticalOffset(offset.Y+48);
    public void PageUp()=>SetVerticalOffset(offset.Y-viewport.Height);
    public void PageDown()=>SetVerticalOffset(offset.Y+viewport.Height);
    public void MouseWheelUp()=>SetVerticalOffset(offset.Y-144);
    public void MouseWheelDown()=>SetVerticalOffset(offset.Y+144);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
}
