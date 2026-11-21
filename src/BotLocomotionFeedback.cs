using System;

namespace ArenaPvP
{
    public sealed class BotLocomotionFeedback
    {
        private bool tracking;
        private float at,x,z,targetX,targetZ;
        public void Clear() { tracking=false; }
        public bool Sample(float now,float px,float pz,float gx,float gz,bool wantsMove,bool grounded)
        {
            float tx=gx-px,tz=gz-pz;
            if(!wantsMove || !grounded || tx*tx+tz*tz<4) { tracking=false;return false; }
            if(!tracking || (gx-targetX)*(gx-targetX)+(gz-targetZ)*(gz-targetZ)>100)
            { tracking=true;at=now;x=px;z=pz;targetX=gx;targetZ=gz;return false; }
            if(now-at<.9f)return false;
            float ax=targetX-x,az=targetZ-z,length=(float)Math.Sqrt(ax*ax+az*az);
            float progress=length>0 ? ((px-x)*ax+(pz-z)*az)/length : 1;
            at=now;x=px;z=pz;targetX=gx;targetZ=gz;
            return progress<.45f;
        }
    }
}
