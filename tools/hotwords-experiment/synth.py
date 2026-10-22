import sherpa_onnx, soundfile as sf, sys, os, json
S=sys.argv[1]
voices={'irina':S+'/models/vits-piper-ru_RU-irina-medium','dmitri':S+'/models/vits-piper-ru_RU-dmitri-medium'}
lines=[l.strip() for l in open('phrases.txt') if l.strip()]
out=[]
for v,d in voices.items():
    onnx=[f for f in os.listdir(d) if f.endswith('.onnx')][0]
    cfg=sherpa_onnx.OfflineTtsConfig(model=sherpa_onnx.OfflineTtsModelConfig(vits=sherpa_onnx.OfflineTtsVitsModelConfig(model=f'{d}/{onnx}',tokens=f'{d}/tokens.txt',data_dir=f'{d}/espeak-ng-data'),num_threads=4))
    tts=sherpa_onnx.OfflineTts(cfg)
    for i,l in enumerate(lines):
        a=tts.generate(l.split('|')[0],sid=0,speed=1.0)
        fn=f'wav/{v}-{i:02d}.wav'; os.makedirs('wav',exist_ok=True)
        sf.write(fn,a.samples,a.sample_rate); out.append((fn,l))
json.dump(out,open('wav/index.json','w'),ensure_ascii=False)
print(len(out))
