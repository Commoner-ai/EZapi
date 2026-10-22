namespace EZapi;

/// <summary>公开Sprite顶点/UV的托管快照。不依赖textureRect或packingRotation枚举。</summary>
internal readonly record struct GuideImagePoint(float X,float Y);
/// <summary>
/// 将Sprite真实三角形映射到最多128×128的透明画布；只读回UV包围区，最长边最多512。
/// Samples中的-1表示三角形外透明像素，其他值指向读回图；不保留原生数组或作者可变集合。
/// </summary>
internal sealed record GuideImageMesh(GuideImageRegion Region,GuideImageBox Crop,int Width,int Height,
    int ReadWidth,int ReadHeight,int[] Samples)
{
    internal const int MaxVertices=4096,MaxIndices=24576,MaxRasterSteps=2000000;
    internal static GuideImageMesh Create(int textureWidth,int textureHeight,float fullWidth,float fullHeight,
        float pivotX,float pivotY,float pixelsPerUnit,IReadOnlyList<GuideImagePoint> vertices,
        IReadOnlyList<GuideImagePoint> uv,IReadOnlyList<ushort> triangles)
    {
        static bool Positive(float value)=>float.IsFinite(value)&&value>0;
        static bool Finite(GuideImagePoint point)=>float.IsFinite(point.X)&&float.IsFinite(point.Y);
        if(textureWidth<=0||textureHeight<=0||!Positive(fullWidth)||!Positive(fullHeight)||!Positive(pixelsPerUnit)||
            !float.IsFinite(pivotX)||!float.IsFinite(pivotY)||vertices is null||uv is null||triangles is null||
            vertices.Count<3||vertices.Count>MaxVertices||vertices.Count!=uv.Count||triangles.Count<3||
            triangles.Count>MaxIndices||triangles.Count%3!=0)
            throw new ArgumentException("EZAPI_GUIDE_IMAGE_MESH: invalid dimensions or bounded mesh arrays.");
        var scale=Math.Min(1,128f/Math.Max(fullWidth,fullHeight));
        var width=Math.Max(1,(int)Math.Round(fullWidth*scale));var height=Math.Max(1,(int)Math.Round(fullHeight*scale));
        var points=new GuideImagePoint[vertices.Count];float minU=1,minV=1,maxU=0,maxV=0;
        for(var i=0;i<points.Length;i++) {
            var v=vertices[i];var u=uv[i];
            if(!Finite(v)||!Finite(u)||u.X<0||u.Y<0||u.X>1||u.Y>1)
                throw new ArgumentException("EZAPI_GUIDE_IMAGE_MESH: nonfinite vertex or UV outside source texture.");
            var x=(v.X*pixelsPerUnit+pivotX)/fullWidth;var y=(v.Y*pixelsPerUnit+pivotY)/fullHeight;
            // 仅容忍原生浮点换算产生的微小边缘误差，不让越界作者几何扩张画布或读取邻居图。
            if(!float.IsFinite(x)||!float.IsFinite(y)||x<-.0001f||y<-.0001f||x>1.0001f||y>1.0001f)
                throw new ArgumentException("EZAPI_GUIDE_IMAGE_MESH: vertex outside original sprite rectangle.");
            points[i]=new(Math.Clamp(x,0,1)*width,Math.Clamp(y,0,1)*height);
            minU=Math.Min(minU,u.X);minV=Math.Min(minV,u.Y);maxU=Math.Max(maxU,u.X);maxV=Math.Max(maxV,u.Y);
        }
        if(maxU<=minU||maxV<=minV)throw new ArgumentException("EZAPI_GUIDE_IMAGE_MESH: empty texture footprint.");
        var left=Math.Clamp((int)Math.Floor((double)minU*textureWidth),0,textureWidth-1);
        var bottom=Math.Clamp((int)Math.Floor((double)minV*textureHeight),0,textureHeight-1);
        var right=Math.Clamp((int)Math.Ceiling((double)maxU*textureWidth),left+1,textureWidth);
        var top=Math.Clamp((int)Math.Ceiling((double)maxV*textureHeight),bottom+1,textureHeight);
        var crop=new GuideImageBox(left,bottom,right-left,top-bottom);
        var readScale=Math.Min(1,512f/Math.Max(crop.Width,crop.Height));
        var readWidth=Math.Max(1,(int)Math.Round(crop.Width*readScale));var readHeight=Math.Max(1,(int)Math.Round(crop.Height*readScale));
        var samples=Enumerable.Repeat(-1,width*height).ToArray();long steps=0;var validTriangles=0;
        for(var t=0;t<triangles.Count;t+=3) {
            var ia=(int)triangles[t];var ib=(int)triangles[t+1];var ic=(int)triangles[t+2];
            if(ia>=points.Length||ib>=points.Length||ic>=points.Length)throw new ArgumentException("EZAPI_GUIDE_IMAGE_MESH: triangle index outside vertices.");
            var a=points[ia];var b=points[ib];var c=points[ic];
            static double Cross(GuideImagePoint p,GuideImagePoint q,double x,double y)=>(q.X-p.X)*(y-p.Y)-(q.Y-p.Y)*(x-p.X);
            var area=Cross(a,b,c.X,c.Y);
            if(Math.Abs(area)<1e-12)continue;
            validTriangles++;
            var x0=Math.Clamp((int)Math.Floor(Math.Min(a.X,Math.Min(b.X,c.X))),0,width-1);
            var x1=Math.Clamp((int)Math.Ceiling(Math.Max(a.X,Math.Max(b.X,c.X))),0,width);
            var y0=Math.Clamp((int)Math.Floor(Math.Min(a.Y,Math.Min(b.Y,c.Y))),0,height-1);
            var y1=Math.Clamp((int)Math.Ceiling(Math.Max(a.Y,Math.Max(b.Y,c.Y))),0,height);
            steps+=(long)(x1-x0)*(y1-y0);
            if(steps>MaxRasterSteps)throw new ArgumentException("EZAPI_GUIDE_IMAGE_MESH_LIMIT: triangle raster budget exceeded.");
            var ua=uv[ia];var ub=uv[ib];var uc=uv[ic];
            for(var y=y0;y<y1;y++)for(var x=x0;x<x1;x++) {
                var wa=Cross(b,c,x+.5,y+.5)/area;var wb=Cross(c,a,x+.5,y+.5)/area;var wc=1-wa-wb;
                if(wa< -1e-8||wb< -1e-8||wc< -1e-8)continue;
                var u=ua.X*wa+ub.X*wb+uc.X*wc;var v=ua.Y*wa+ub.Y*wb+uc.Y*wc;
                var sx=Math.Clamp((int)Math.Floor((u*textureWidth-left)/crop.Width*readWidth),0,readWidth-1);
                var sy=Math.Clamp((int)Math.Floor((v*textureHeight-bottom)/crop.Height*readHeight),0,readHeight-1);
                samples[y*width+x]=sy*readWidth+sx;
            }
        }
        if(validTriangles==0)throw new ArgumentException("EZAPI_GUIDE_IMAGE_MESH: no nondegenerate triangles.");
        return new(new(new(0,0,1,1),fullWidth,fullHeight,0,0,fullWidth,fullHeight),crop,width,height,readWidth,readHeight,samples);
    }
}
