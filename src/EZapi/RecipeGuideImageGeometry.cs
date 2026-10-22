namespace EZapi;

/// <summary>纯托管图集裁切：UV使用纹理左下原点，GUI使用屏幕左上原点。保留精灵裁切前透明边距与比例。</summary>
internal readonly record struct GuideImageBox(float X,float Y,float Width,float Height);
internal sealed record GuideImageRegion(GuideImageBox Uv,float Width,float Height,float OffsetX,float OffsetY,float FullWidth,float FullHeight)
{
    internal GuideImageBox Fit(GuideImageBox box)
    {
        if(!Valid(box.Width)||!Valid(box.Height)||!float.IsFinite(box.X)||!float.IsFinite(box.Y))throw new ArgumentException("EZAPI_GUIDE_IMAGE: invalid display box");
        var scale=Math.Min(box.Width/FullWidth,box.Height/FullHeight);
        return new(box.X+(box.Width-FullWidth*scale)/2+OffsetX*scale,
            box.Y+(box.Height-FullHeight*scale)/2+(FullHeight-OffsetY-Height)*scale,Width*scale,Height*scale);
    }
    internal static GuideImageRegion Create(float textureWidth,float textureHeight,GuideImageBox crop,float fullWidth,float fullHeight,float offsetX,float offsetY,int rotation)
    {
        if(!Valid(textureWidth)||!Valid(textureHeight)||!Valid(crop.Width)||!Valid(crop.Height)||!Valid(fullWidth)||!Valid(fullHeight)||
            !float.IsFinite(crop.X)||!float.IsFinite(crop.Y)||crop.X<0||crop.Y<0||crop.X+crop.Width>textureWidth||crop.Y+crop.Height>textureHeight||
            !float.IsFinite(offsetX)||!float.IsFinite(offsetY)||offsetX<0||offsetY<0||offsetX+crop.Width>fullWidth||offsetY+crop.Height>fullHeight||rotation is <0 or >3)
            throw new ArgumentException("EZAPI_GUIDE_IMAGE: invalid or unsupported atlas crop");
        var u=crop.X/textureWidth;var v=crop.Y/textureHeight;var w=crop.Width/textureWidth;var h=crop.Height/textureHeight;
        if(rotation is 1 or 3){u+=w;w=-w;}if(rotation is 2 or 3){v+=h;h=-h;}
        return new(new(u,v,w,h),crop.Width,crop.Height,offsetX,offsetY,fullWidth,fullHeight);
    }
    private static bool Valid(float value)=>float.IsFinite(value)&&value>0;
}
