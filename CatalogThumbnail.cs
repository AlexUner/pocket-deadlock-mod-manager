using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PocketDeadlock;

public sealed class CatalogThumbnail : Grid
{
    public static readonly DependencyProperty UrlProperty=DependencyProperty.Register(nameof(Url),typeof(string),typeof(CatalogThumbnail),new PropertyMetadata("",(d,_)=>((CatalogThumbnail)d).Reset()));
    public string Url {get=>(string)GetValue(UrlProperty);set=>SetValue(UrlProperty,value);}
    public static readonly DependencyProperty ViewportHeightProperty=DependencyProperty.Register(nameof(ViewportHeight),typeof(double),typeof(CatalogThumbnail),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double ViewportHeight {get=>(double)GetValue(ViewportHeightProperty);set=>SetValue(ViewportHeightProperty,value);}
    readonly Image image=new() {Stretch=Stretch.UniformToFill,Visibility=Visibility.Collapsed};
    readonly TextBlock status=new() {FontSize=12,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8,6,8,0)};
    readonly StackPanel placeholder=new() {HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    ScrollViewer? viewer;
    CancellationTokenSource? request;
    bool queued;
    int generation;
    public bool HasImage=>image.Source!=null;
    public CatalogThumbnail()
    {
        IsHitTestVisible=false;SetResourceReference(BackgroundProperty,"Input");
        var icon=new TextBlock {Text="\uEB9F",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=28,HorizontalAlignment=HorizontalAlignment.Center};
        icon.SetResourceReference(TextBlock.ForegroundProperty,"Muted");status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        placeholder.Children.Add(icon);placeholder.Children.Add(status);Children.Add(placeholder);Children.Add(image);
        Loaded+=(_,_)=>
        {
            DependencyObject? parent=this;
            while(parent!=null && parent is not ScrollViewer) parent=VisualTreeHelper.GetParent(parent);
            viewer=parent as ScrollViewer;if(viewer!=null) {viewer.ScrollChanged+=Scrolled;viewer.SizeChanged+=Resized;}
            Queue();
        };
        Unloaded+=(_,_)=> {if(viewer!=null) {viewer.ScrollChanged-=Scrolled;viewer.SizeChanged-=Resized;}viewer=null;Stop();};
        IsVisibleChanged+=(_,_)=> {if(IsVisible) Queue();else Stop();};Reset();
    }
    void Scrolled(object sender,ScrollChangedEventArgs e)=>Queue();
    void Resized(object sender,SizeChangedEventArgs e)=>Queue();
    void Reset() {Stop();image.Source=null;image.Visibility=Visibility.Collapsed;placeholder.Visibility=Visibility.Visible;status.Text=L.T(Url==""?"Нет обложки":"Загружаем обложку…");Queue();}
    void Stop() {generation++;request?.Cancel();request?.Dispose();request=null;}
    void Queue()
    {
        if(queued) return;queued=true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{queued=false;CheckViewport();}));
    }
    void CheckViewport()
    {
        if(!IsLoaded || !IsVisible || HasImage || Url=="" || App.Thumbnails==null) return;
        bool visible=true;
        if(viewer!=null)
        {
            var bounds=TransformToAncestor(viewer).TransformBounds(new Rect(0,0,ActualWidth,ActualHeight));
            // Match the virtual panel's overscan so the next two rows are ready before scrolling.
            double ahead=Math.Max(240,(ActualHeight+128)*2);
            visible=bounds.IntersectsWith(new Rect(0,-ahead,viewer.ActualWidth,viewer.ActualHeight+ahead*2));
        }
        if(!visible) {if(request!=null) Stop();return;}
        if(request!=null) return;
        request=new CancellationTokenSource();int expected=++generation;_=Load(expected,request.Token);
    }
    async Task Load(int expected,CancellationToken token)
    {
        try
        {
            var bitmap=await App.Thumbnails!.Get(Url,token);
            if(expected!=generation || !IsLoaded) return;
            if(bitmap==null) {status.Text=L.T("Обложка недоступна");return;}
            image.Source=bitmap;image.Visibility=Visibility.Visible;placeholder.Visibility=Visibility.Collapsed;
        }
        catch(OperationCanceledException) { }
    }
    protected override Size MeasureOverride(Size constraint)
    {
        double width=double.IsFinite(constraint.Width)?constraint.Width:240;
        double height=width*9/16;if(ViewportHeight>0) height=Math.Min(height,Math.Max(72,ViewportHeight-120));
        var size=new Size(width,height);base.MeasureOverride(size);return size;
    }
    protected override Size ArrangeOverride(Size size) {image.Stretch=size.Height<size.Width*.45?Stretch.Uniform:Stretch.UniformToFill;Clip=new RectangleGeometry(new Rect(size),3,3);return base.ArrangeOverride(size);}
}
