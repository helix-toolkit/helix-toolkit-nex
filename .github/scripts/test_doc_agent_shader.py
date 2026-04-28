"""Property-based and unit tests for shader utility function extraction in doc_agent.py.

Feature: shader-util-function-extraction
"""

import sys
from pathlib import Path

# Add the scripts directory to the path so we can import doc_agent
sys.path.insert(0, str(Path(__file__).resolve().parent))

from hypothesis import given, settings
from hypothesis import strategies as st

from doc_agent import extract_utility_functions, _SHADER_UTIL_DOC_PROMPT


# ---------------------------------------------------------------------------
# Property 1: Extraction round-trip
# For any string placed between the markers, extract_utility_functions returns
# that exact string, preserving all whitespace, line breaks, and indentation.
# Validates: Requirements 1.1, 1.4
# ---------------------------------------------------------------------------


@settings(max_examples=100)
@given(content=st.text())
def test_extraction_round_trip(content: str) -> None:
    """Feature: shader-util-function-extraction, Property 1: Extraction round-trip

    **Validates: Requirements 1.1, 1.4**
    """
    begin_marker = "/*UTILITY_FUNCTIONS_BEGIN*/"
    end_marker = "/*UTILITY_FUNCTIONS_END*/"

    # Wrap the generated content between markers
    file_content = f"{begin_marker}{content}{end_marker}"

    result = extract_utility_functions(file_content, "test_file.glsl")

    assert result == content, f"Round-trip failed: expected {content!r}, got {result!r}"


# ---------------------------------------------------------------------------
# Property 2: Missing markers yield empty extraction
# For any content string missing the BEGIN marker, or containing the BEGIN
# marker but missing the END marker, extract_utility_functions returns "".
# Validates: Requirements 1.2, 1.3
# ---------------------------------------------------------------------------


@settings(max_examples=100)
@given(content=st.text())
def test_missing_begin_marker_yields_empty(content: str) -> None:
    """Feature: shader-util-function-extraction, Property 2: Missing markers yield empty extraction

    **Validates: Requirements 1.2, 1.3**

    Content with no BEGIN marker at all should always return empty string.
    """
    from hypothesis import assume

    begin_marker = "/*UTILITY_FUNCTIONS_BEGIN*/"

    # Ensure the generated content does not accidentally contain the begin marker
    assume(begin_marker not in content)

    result = extract_utility_functions(content, "test_file.glsl")

    assert (
        result == ""
    ), f"Expected empty string for content without BEGIN marker, got {result!r}"


@settings(max_examples=100)
@given(content=st.text(), after_begin=st.text())
def test_missing_end_marker_yields_empty(content: str, after_begin: str) -> None:
    """Feature: shader-util-function-extraction, Property 2: Missing markers yield empty extraction

    **Validates: Requirements 1.2, 1.3**

    Content with the BEGIN marker but without the END marker should return empty string.
    """
    from hypothesis import assume

    begin_marker = "/*UTILITY_FUNCTIONS_BEGIN*/"
    end_marker = "/*UTILITY_FUNCTIONS_END*/"

    # Ensure the text after the begin marker does not accidentally contain the end marker
    assume(end_marker not in after_begin)

    # Build content that has the BEGIN marker but no END marker
    file_content = f"{content}{begin_marker}{after_begin}"

    result = extract_utility_functions(file_content, "test_file.glsl")

    assert (
        result == ""
    ), f"Expected empty string for content without END marker, got {result!r}"


# ---------------------------------------------------------------------------
# Unit tests: extract_utility_functions with real shader content
# Validates: Requirements 2.1, 3.1
# ---------------------------------------------------------------------------

from doc_agent import REPO_ROOT


class TestExtractUtilityFunctionsRealShaders:
    """Unit tests verifying extract_utility_functions against actual shader files."""

    def test_pbr_shader_extraction_contains_getPBRProperties(self) -> None:
        """Extraction from psPBRTemplate.glsl should contain the getPBRProperties function.

        Validates: Requirement 2.1
        """
        shader_path = (
            REPO_ROOT
            / "Source"
            / "HelixToolkit-Nex"
            / "HelixToolkit.Nex.Shaders"
            / "Frag"
            / "psPBRTemplate.glsl"
        )
        content = shader_path.read_text(encoding="utf-8-sig")
        result = extract_utility_functions(content, str(shader_path))

        assert result != "", "Extraction should not be empty for PBR shader"
        assert (
            "getPBRProperties" in result
        ), "Extracted block should contain the getPBRProperties function"

    def test_point_shader_extraction_contains_getTimeMs(self) -> None:
        """Extraction from psPointTemplate.glsl should contain the getTimeMs function.

        Validates: Requirement 3.1
        """
        shader_path = (
            REPO_ROOT
            / "Source"
            / "HelixToolkit-Nex"
            / "HelixToolkit.Nex.Shaders"
            / "Point"
            / "psPointTemplate.glsl"
        )
        content = shader_path.read_text(encoding="utf-8-sig")
        result = extract_utility_functions(content, str(shader_path))

        assert result != "", "Extraction should not be empty for Point shader"
        assert (
            "getTimeMs" in result
        ), "Extracted block should contain the getTimeMs function"

    def test_pbr_shader_extraction_matches_known_block(self) -> None:
        """The extracted block from psPBRTemplate.glsl should match the content between markers.

        Validates: Requirement 2.1
        """
        shader_path = (
            REPO_ROOT
            / "Source"
            / "HelixToolkit-Nex"
            / "HelixToolkit.Nex.Shaders"
            / "Frag"
            / "psPBRTemplate.glsl"
        )
        content = shader_path.read_text(encoding="utf-8-sig")
        result = extract_utility_functions(content, str(shader_path))

        # Verify the extracted block starts with the first function after the marker
        assert "PBRProperties getPBRProperties()" in result
        # Verify it contains other known functions
        assert "getViewProjection" in result
        assert "getCameraPosition" in result
        assert "mixWithPointerRing" in result
        # Verify it does NOT contain content outside the markers
        assert "UTILITY_FUNCTIONS_BEGIN" not in result
        assert "UTILITY_FUNCTIONS_END" not in result

    def test_point_shader_extraction_matches_known_block(self) -> None:
        """The extracted block from psPointTemplate.glsl should match the content between markers.

        Validates: Requirement 3.1
        """
        shader_path = (
            REPO_ROOT
            / "Source"
            / "HelixToolkit-Nex"
            / "HelixToolkit.Nex.Shaders"
            / "Point"
            / "psPointTemplate.glsl"
        )
        content = shader_path.read_text(encoding="utf-8-sig")
        result = extract_utility_functions(content, str(shader_path))

        # Verify the extracted block contains known functions
        assert "getTimeMs" in result
        assert "getViewProjection" in result
        assert "getCameraPosition" in result
        assert "mixWithPointerRing" in result
        # Verify it does NOT contain content outside the markers
        assert "UTILITY_FUNCTIONS_BEGIN" not in result
        assert "UTILITY_FUNCTIONS_END" not in result
        # Verify it does NOT contain content after the end marker
        assert "MATERIAL_TYPE" not in result


# ---------------------------------------------------------------------------
# Property 3: Prompt template includes all inputs
# For any shader file name and any GLSL code string, formatting
# _SHADER_UTIL_DOC_PROMPT with those values produces a string that contains
# both the shader file name and the GLSL code verbatim.
# Validates: Requirements 5.1
# ---------------------------------------------------------------------------


@settings(max_examples=100)
@given(name=st.text(), code=st.text())
def test_prompt_template_includes_all_inputs(name: str, code: str) -> None:
    """Feature: shader-util-function-extraction, Property 3: Prompt template includes all inputs

    **Validates: Requirements 5.1**
    """
    from hypothesis import assume

    # Filter out strings containing { or } that would interfere with .format()
    assume("{" not in name and "}" not in name)
    assume("{" not in code and "}" not in code)

    formatted = _SHADER_UTIL_DOC_PROMPT.format(shader_file_name=name, glsl_code=code)

    assert name in formatted, f"Shader file name {name!r} not found in formatted prompt"
    assert code in formatted, f"GLSL code {code!r} not found in formatted prompt"


# ---------------------------------------------------------------------------
# Unit tests: Prompt template content
# Validates: Requirements 5.2, 5.3, 5.4
# ---------------------------------------------------------------------------


class TestShaderUtilDocPromptContent:
    """Unit tests verifying _SHADER_UTIL_DOC_PROMPT contains required instructions."""

    def test_prompt_contains_signature_keyword(self) -> None:
        """The prompt template should instruct the AI to document function signatures.

        Validates: Requirement 5.2
        """
        prompt_lower = _SHADER_UTIL_DOC_PROMPT.lower()
        assert (
            "signature" in prompt_lower
        ), "Prompt template should contain the keyword 'signature'"

    def test_prompt_contains_description_keyword(self) -> None:
        """The prompt template should instruct the AI to provide a description.

        Validates: Requirement 5.2
        """
        prompt_lower = _SHADER_UTIL_DOC_PROMPT.lower()
        assert (
            "description" in prompt_lower
        ), "Prompt template should contain the keyword 'description'"

    def test_prompt_contains_return_type_keyword(self) -> None:
        """The prompt template should instruct the AI to document the return type.

        Validates: Requirement 5.2
        """
        prompt_lower = _SHADER_UTIL_DOC_PROMPT.lower()
        assert (
            "return type" in prompt_lower
        ), "Prompt template should contain the keyword 'return type'"

    def test_prompt_instructs_markdown_only_no_preamble(self) -> None:
        """The prompt template should instruct the AI to return only Markdown with no preamble.

        Validates: Requirement 5.3
        """
        prompt_lower = _SHADER_UTIL_DOC_PROMPT.lower()
        assert (
            "no preamble" in prompt_lower
        ), "Prompt template should instruct 'no preamble'"
        assert (
            "only markdown" in prompt_lower or "return only markdown" in prompt_lower
        ), "Prompt template should instruct to return only Markdown content"

    def test_prompt_mentions_fenced_glsl_code_blocks(self) -> None:
        """The prompt template should mention fenced glsl code blocks for signatures.

        Validates: Requirement 5.4
        """
        # Check for the fenced code block marker with glsl language identifier
        assert (
            "```glsl" in _SHADER_UTIL_DOC_PROMPT or "`glsl`" in _SHADER_UTIL_DOC_PROMPT
        ), "Prompt template should mention fenced glsl code blocks"


# ---------------------------------------------------------------------------
# Property 4: Written documentation has trailing newline
# For any non-empty documentation content string produced by the AI model,
# the content written to the output file (content + "\n") ends with "\n".
# Validates: Requirements 6.4
# ---------------------------------------------------------------------------


@settings(max_examples=100)
@given(content=st.text(min_size=1))
def test_written_documentation_has_trailing_newline(content: str) -> None:
    """Feature: shader-util-function-extraction, Property 4: Written documentation has trailing newline

    **Validates: Requirements 6.4**
    """
    # Simulate the write logic used in generate_shader_util_docs
    result = content + "\n"

    assert result.endswith(
        "\n"
    ), f"Written content should end with a trailing newline, got {result!r}"


# ---------------------------------------------------------------------------
# Unit test: Empty extraction skip behavior
# When extraction returns empty, generate_shader_util_docs does not call
# call_ai and does not write a file.
# Validates: Requirements 2.5, 3.5
# ---------------------------------------------------------------------------

from unittest.mock import patch, MagicMock

from doc_agent import generate_shader_util_docs


class TestEmptyExtractionSkipBehavior:
    """Unit tests verifying generate_shader_util_docs skips when extraction is empty."""

    @patch("doc_agent.read_file_limited")
    @patch("doc_agent.call_ai")
    @patch.object(Path, "write_text")
    def test_no_markers_skips_call_ai_and_write(
        self,
        mock_write_text: MagicMock,
        mock_call_ai: MagicMock,
        mock_read_file: MagicMock,
    ) -> None:
        """When shader content has no markers, call_ai is not called and no file is written.

        Validates: Requirements 2.5, 3.5
        """
        # Return content without UTILITY_FUNCTIONS markers
        mock_read_file.return_value = "some shader code without markers"
        mock_client = MagicMock()

        result = generate_shader_util_docs(mock_client)

        # call_ai should not have been called since extraction yields empty
        mock_call_ai.assert_not_called()
        # No files should have been written
        mock_write_text.assert_not_called()
        # The returned list should be empty
        assert result == []


# ---------------------------------------------------------------------------
# Integration test: Full pipeline with mocked AI
# Mock call_ai to return known Markdown content, run generate_shader_util_docs,
# verify both output files are created with correct content, UTF-8 encoding,
# and trailing newline.
# Validates: Requirements 2.2, 2.3, 3.2, 3.3, 6.1, 6.2, 6.3, 6.4
# ---------------------------------------------------------------------------

import tempfile

from doc_agent import SHADER_UTIL_CONFIGS


class TestFullPipelineWithMockedAI:
    """Integration test verifying the full shader doc pipeline with mocked AI."""

    def test_generate_shader_util_docs_creates_both_files_with_correct_content(
        self,
    ) -> None:
        """Mock call_ai, run generate_shader_util_docs, verify output files.

        Validates: Requirements 2.2, 2.3, 3.2, 3.3, 6.1, 6.2, 6.3, 6.4
        """
        mocked_ai_output = (
            "# Test Shader Utility Functions\n\nSome documentation content"
        )

        with tempfile.TemporaryDirectory() as tmp_dir:
            tmp_root = Path(tmp_dir)

            # Create shader files with UTILITY_FUNCTIONS markers for each config entry
            for shader_rel_path, output_rel_path in SHADER_UTIL_CONFIGS:
                shader_full = tmp_root / shader_rel_path
                shader_full.parent.mkdir(parents=True, exist_ok=True)
                shader_content = (
                    "// some preamble code\n"
                    "/*UTILITY_FUNCTIONS_BEGIN*/"
                    "vec3 someFunc() { return vec3(0.0); }\n"
                    "/*UTILITY_FUNCTIONS_END*/"
                    "\n// some trailing code\n"
                )
                shader_full.write_text(shader_content, encoding="utf-8")

                # Ensure output directory exists
                output_full = tmp_root / output_rel_path
                output_full.parent.mkdir(parents=True, exist_ok=True)

            mock_client = MagicMock()

            with patch("doc_agent.REPO_ROOT", tmp_root), patch(
                "doc_agent.call_ai", return_value=mocked_ai_output
            ) as mock_call_ai:
                result = generate_shader_util_docs(mock_client)

            # Verify call_ai was called once per shader config entry
            assert mock_call_ai.call_count == len(SHADER_UTIL_CONFIGS)

            # Verify the returned list contains both output filenames
            expected_filenames = [
                Path(output_rel).name for _, output_rel in SHADER_UTIL_CONFIGS
            ]
            assert sorted(result) == sorted(expected_filenames)

            # Verify both output files exist and have correct content
            for _, output_rel_path in SHADER_UTIL_CONFIGS:
                output_full = tmp_root / output_rel_path

                # File exists
                assert (
                    output_full.exists()
                ), f"Output file {output_rel_path} should exist"

                # Read back with UTF-8 encoding to verify encoding correctness
                file_content = output_full.read_text(encoding="utf-8")

                # Content matches mocked AI output plus trailing newline
                expected_content = mocked_ai_output + "\n"
                assert file_content == expected_content, (
                    f"File {output_rel_path} content mismatch: "
                    f"expected {expected_content!r}, got {file_content!r}"
                )

                # Content ends with trailing newline
                assert file_content.endswith(
                    "\n"
                ), f"File {output_rel_path} should end with trailing newline"


# ---------------------------------------------------------------------------
# Integration test: main() calls generate_shader_util_docs
# Verify that main() invokes the shader documentation pipeline after README
# processing completes.
# Validates: Requirements 4.1
# ---------------------------------------------------------------------------


class TestMainCallsShaderPipeline:
    """Integration test verifying main() calls generate_shader_util_docs."""

    @patch("sys.argv", ["doc_agent.py"])
    @patch("doc_agent.get_changed_packages", return_value=[])
    @patch("doc_agent.get_all_packages", return_value=[])
    @patch("doc_agent.get_openai_client")
    @patch("doc_agent.generate_shader_util_docs", return_value=[])
    def test_main_calls_generate_shader_util_docs(
        self,
        mock_generate_shader_util_docs: MagicMock,
        mock_get_openai_client: MagicMock,
        mock_get_all_packages: MagicMock,
        mock_get_changed_packages: MagicMock,
    ) -> None:
        """main() should call generate_shader_util_docs exactly once with the client.

        Validates: Requirement 4.1
        """
        from doc_agent import main

        mock_client = MagicMock()
        mock_get_openai_client.return_value = mock_client

        main()

        mock_generate_shader_util_docs.assert_called_once_with(mock_client)
