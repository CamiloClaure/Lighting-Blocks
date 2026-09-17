using System;
using System.IO;
using Unity.Collections;
using UnityEngine;
namespace LightingBlocks
{
    // Explicit evidence mode only: render the real mixer on the same clock as video frames.
    public sealed class TwilightAudioCapture:IDisposable
    {
        BinaryWriter writer;long samples;readonly int rate;bool active;
        public TwilightAudioCapture(string path)
        {
            rate=AudioSettings.outputSampleRate;active=AudioRenderer.Start();
            if(!active)throw new InvalidOperationException("Audio recording could not start");
            writer=new BinaryWriter(File.Create(path));Header(0);
        }
        void Header(int bytes){writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+bytes);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)3);writer.Write((short)2);writer.Write(rate);writer.Write(rate*8);writer.Write((short)8);writer.Write((short)32);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(bytes);}
        public void Frame(){int count=AudioRenderer.GetSampleCountForCaptureFrame();if(count<=0)return;using(var buffer=new NativeArray<float>(count*2,Allocator.Temp)){if(!AudioRenderer.Render(buffer))throw new InvalidOperationException("Audio capture failed");for(int i=0;i<buffer.Length;i++)writer.Write(buffer[i]);samples+=buffer.Length;}}
        public void Dispose(){if(!active)return;AudioRenderer.Stop();active=false;writer.Seek(0,SeekOrigin.Begin);Header(checked((int)samples*4));writer.Dispose();}
    }
}
