"""Экспорт GigaAM v3 e2e RNNT (исходной или дообученной) в формат модели приложения (sherpa-onnx).
python tools/finetune/export_sherpa.py <v3_e2e_rnnt | путь к .ckpt из train_utils/train.py> <папка вывода>

Повторяет scripts/nemo/GigaAM/export-onnx-rnnt-v3-punct.py из k2-fsa/sherpa-onnx (им собрана
sherpa-onnx-nemo-transducer-punct-giga-am-v3-russian-2025-12-16): encoder.int8.onnx, decoder.onnx,
joiner.onnx, tokens.txt. Работает на CPU. Нужны gigaam, onnx, onnxruntime.
"""
import os, sys

import gigaam
import onnx
import torch
from gigaam.utils import onnx_converter
from onnxruntime.quantization import QuantType, quantize_dynamic
from torch import Tensor


def add_meta_data(filename, meta_data):
    model = onnx.load(filename)
    while len(model.metadata_props):
        model.metadata_props.pop()
    for key, value in meta_data.items():
        meta = model.metadata_props.add()
        meta.key, meta.value = key, str(value)
    onnx.save(model, filename)


class DecoderWrapper(torch.nn.Module):
    def __init__(self, m):
        super().__init__()
        self.m = m

    def forward(self, x: Tensor, unused_x_len: Tensor, h: Tensor, c: Tensor):
        emb = self.m.head.decoder.embed(x)
        g, (h, c) = self.m.head.decoder.lstm(emb.transpose(0, 1), (h, c))
        return g.permute(1, 2, 0), unused_x_len + 1, h, c


def main(source, out):
    os.makedirs(out, exist_ok=True)
    model = gigaam.load_model(source, fp16_encoder=False, device="cpu")
    model.eval()
    name = "model"

    sp = model.decoding.tokenizer.model
    with open(os.path.join(out, "tokens.txt"), "w", encoding="utf-8", newline="\n") as f:
        for i in range(sp.vocab_size()):
            f.write(f"{sp.id_to_piece(i)} {i}\n")
        f.write(f"<blk> {sp.vocab_size()}\n")

    onnx_converter(model_name=f"{name}_encoder", out_dir=out, module=model.encoder, dynamic_axes=model.encoder.dynamic_axes())
    # Входы размером 1, как подаёт sherpa-onnx: input_example() в новых gigaam даёт пачку из 8,
    # и декодер с joiner экспортируются с жёсткой формой [8, …] (sherpa-onnx падает на загрузке)
    pred_hidden = model.cfg["head"]["decoder"]["pred_hidden"]
    hidden = torch.zeros(model.cfg["head"]["decoder"]["pred_rnn_layers"], 1, pred_hidden)
    onnx_converter(
        model_name=f"{name}_decoder", out_dir=out, module=DecoderWrapper(model),
        dynamic_axes=model.encoder.dynamic_axes(),
        inputs=(torch.zeros(1, 1, dtype=torch.int32), torch.zeros(1, dtype=torch.int32), hidden, hidden.clone()),
        input_names=["x", "unused_x_len.1", "h.1", "c.1"], output_names=["dec", "unused_x_len", "h", "c"],
    )
    enc_hidden = model.cfg["head"]["joint"]["enc_hidden"]
    onnx_converter(
        model_name=f"{name}_joint", out_dir=out, module=model.head.joint,
        inputs=(torch.zeros(1, enc_hidden, 1), torch.zeros(1, pred_hidden, 1)),
        input_names=["enc", "dec"], output_names=["joint"],
    )

    encoder = os.path.join(out, f"{name}_encoder.onnx")
    add_meta_data(encoder, {
        "vocab_size": model.cfg["head"]["decoder"]["num_classes"] - 1,
        "pred_rnn_layers": model.cfg["head"]["decoder"]["pred_rnn_layers"],
        "pred_hidden": model.cfg["head"]["decoder"]["pred_hidden"],
        "normalize_type": "",
        "subsampling_factor": 4,
        "model_type": "EncDecRNNTBPEModel",
        "version": "3",
        "model_author": "https://github.com/salute-developers/GigaAM",
        "license": "https://github.com/salute-developers/GigaAM/blob/main/LICENSE",
        "language": "Russian",
        "comment": f"v3, {os.path.basename(source)}",
        "is_giga_am": 1,
    })
    quantize_dynamic(model_input=encoder, model_output=os.path.join(out, "encoder.int8.onnx"), weight_type=QuantType.QUInt8)
    os.replace(os.path.join(out, f"{name}_decoder.onnx"), os.path.join(out, "decoder.onnx"))
    os.replace(os.path.join(out, f"{name}_joint.onnx"), os.path.join(out, "joiner.onnx"))
    os.remove(encoder)
    for extra in os.listdir(out):
        if extra.endswith(".data"):  # внешние веса больших моделей после quantize уже не нужны
            os.remove(os.path.join(out, extra))
    print(f"готово: {out}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
