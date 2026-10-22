import sherpa_onnx, soundfile as sf, sys, json, time, numpy as np
G,mode,score=sys.argv[1],sys.argv[2],float(sys.argv[3]) if len(sys.argv)>3 else 0
kw=dict(encoder=f'{G}/encoder.int8.onnx',decoder=f'{G}/decoder.onnx',joiner=f'{G}/joiner.onnx',tokens=f'{G}/tokens.txt',num_threads=4,model_type='nemo_transducer')
if mode=='greedy': r=sherpa_onnx.OfflineRecognizer.from_transducer(decoding_method='greedy_search',**kw)
elif mode=='beam': r=sherpa_onnx.OfflineRecognizer.from_transducer(decoding_method='modified_beam_search',max_active_paths=int(__import__("os").environ.get("PATHS","4")),**kw)
else: r=sherpa_onnx.OfflineRecognizer.from_transducer(decoding_method='modified_beam_search',max_active_paths=int(__import__("os").environ.get("PATHS","4")),hotwords_file='hotwords.txt',hotwords_score=score,modeling_unit='bpe',bpe_vocab='bpe.vocab',**kw)
idx=json.load(open('wav/index.json'))
res={}
t0=time.time()
for fn,line in idx:
    a,sr=sf.read(fn,dtype='float32')
    s=r.create_stream(); s.accept_waveform(sr,a); r.decode_stream(s); res[fn]=s.result.text
el=time.time()-t0
json.dump(res,open(f'res-{mode}-{score}.json','w'),ensure_ascii=False,indent=0)
# long audio timing
a=np.concatenate([sf.read(fn,dtype='float32')[0] for fn,_ in idx]); sr=sf.read(idx[0][0])[1]
for secs in (5,15,45):
    x=a[:int(secs*sr)]; best=1e9
    for _ in range(3):
        t=time.time(); s=r.create_stream(); s.accept_waveform(sr,x); r.decode_stream(s); best=min(best,time.time()-t)
    print(mode,score,f'{secs}s: {best*1000:.0f} ms')
print(mode,score,'all phrases',f'{el:.2f}s')
