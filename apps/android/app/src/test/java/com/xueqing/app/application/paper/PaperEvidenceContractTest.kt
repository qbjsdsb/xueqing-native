package com.xueqing.app.application.paper

import java.time.LocalDate
import java.util.UUID
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Test

class PaperEvidenceContractTest {
    @Test
    fun paperKind_roundTripsWireValuesAndRejectsUnknown() {
        assertEquals(PaperKind.Homework, PaperKind.fromWireValue("homework"))
        assertEquals(PaperKind.Quiz, PaperKind.fromWireValue("quiz"))
        assertEquals(PaperKind.UnitTest, PaperKind.fromWireValue("unit_test"))
        assertEquals(PaperKind.Exam, PaperKind.fromWireValue("exam"))
        assertNull(PaperKind.fromWireValue("unknown_kind"))
    }

    @Test
    fun headerRedactionPolicy_enforcesSafeRatioBounds() {
        val enabled = HeaderRedactionPolicy(enabled = true)
        assertEquals(HeaderRedactionPolicy.DEFAULT_TOP_BAND_RATIO, enabled.effectiveRatio(), 0.0001f)

        val disabled = HeaderRedactionPolicy(enabled = false, topBandRatio = 0.18f)
        assertEquals(0f, disabled.effectiveRatio(), 0.0001f)

        assertThrows(IllegalArgumentException::class.java) {
            HeaderRedactionPolicy(enabled = true, topBandRatio = 0.01f)
        }
        assertThrows(IllegalArgumentException::class.java) {
            HeaderRedactionPolicy(enabled = true, topBandRatio = 0.60f)
        }
    }

    @Test
    fun paperCaptureMetadata_encodesPaperFieldsAndRedactedPagesOnlyWhenPresent() {
        val metadata = PaperCaptureMetadata(
            enabled = true,
            paperDate = LocalDate.of(2026, 11, 22),
            paperKind = PaperKind.UnitTest,
            title = "  第三单元小数除法  ",
            headerRedactionEnabled = true,
        ).toClientCaptureMetadata(
            pageCount = 3,
            redactedPageIndices = listOf(3, 1, 1),
        )

        assertEquals("quick_capture", metadata["surface"])
        assertEquals("paper_evidence", metadata["capture_mode"])
        assertEquals("2026-11-22", metadata["paper_date"])
        assertEquals("unit_test", metadata["paper_kind"])
        assertEquals("第三单元小数除法", metadata["paper_title"])
        assertEquals("3", metadata["attachment_page_count"])
        assertEquals("1,3", metadata["header_redacted_pages"])
        assertEquals("true", metadata["header_redaction_enabled"])
    }

    @Test
    fun createPaperSubmissionRequest_rejectsEmptyOrDuplicatePages() {
        val id = UUID(0, 1)
        val att1 = UUID(0, 11)
        val att2 = UUID(0, 12)

        assertThrows(IllegalArgumentException::class.java) {
            CreatePaperSubmissionRequest(
                operationId = id,
                organizationId = id,
                studentId = id,
                subjectProfileId = id,
                assignmentId = id,
                paperDate = LocalDate.of(2026, 11, 22),
                paperKind = PaperKind.Homework,
                pages = emptyList(),
            )
        }

        assertThrows(IllegalArgumentException::class.java) {
            CreatePaperSubmissionRequest(
                operationId = id,
                organizationId = id,
                studentId = id,
                subjectProfileId = id,
                assignmentId = id,
                paperDate = LocalDate.of(2026, 11, 22),
                paperKind = PaperKind.Homework,
                pages = listOf(
                    PaperPageInput(pageIndex = 1, attachmentId = att1, headerRedacted = true),
                    PaperPageInput(pageIndex = 1, attachmentId = att2, headerRedacted = false),
                ),
            )
        }

        assertThrows(IllegalArgumentException::class.java) {
            CreatePaperSubmissionRequest(
                operationId = id,
                organizationId = id,
                studentId = id,
                subjectProfileId = id,
                assignmentId = id,
                paperDate = LocalDate.of(2026, 11, 22),
                paperKind = PaperKind.Homework,
                pages = listOf(
                    PaperPageInput(pageIndex = 1, attachmentId = att1, headerRedacted = true),
                    PaperPageInput(pageIndex = 2, attachmentId = att1, headerRedacted = false),
                ),
            )
        }
    }
}
