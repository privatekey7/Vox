"""Model catalog sanity: both models are offered, GigaAM is the Russian default."""

from voxapp.registry import DEFAULT_MODEL_ID, MODELS, RECOMMENDED_MODEL_ID


def test_both_models_are_in_catalog():
    assert {"gigaam-v3-rnnt-punct", "parakeet-tdt-0.6b-v3"} <= set(MODELS)


def test_gigaam_is_recommended_default():
    assert DEFAULT_MODEL_ID == RECOMMENDED_MODEL_ID == "gigaam-v3-rnnt-punct"


def test_every_model_entry_has_required_fields():
    required = {
        "name",
        "engine",
        "engine_subtype",
        "language",
        "size_mb",
        "url",
        "archive_format",
        "extracted_dir",
        "files",
        "needs_itn",
    }
    for mid, info in MODELS.items():
        assert required <= set(info), f"{mid} misses {required - set(info)}"
        assert info["url"].endswith(info["extracted_dir"] + ".tar.bz2")
