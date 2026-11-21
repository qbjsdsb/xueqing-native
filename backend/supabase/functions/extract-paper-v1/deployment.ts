// Non-secret, auditable deployment policy for Xueqing Paper Vision Extraction v1.
// Region or default-model changes are production-topology changes and must go through Git/PR/CI.
export const DEFAULT_HOSTED_REQUIRED_EDGE_REGION = "ap-southeast-1";
export const DEFAULT_GEMINI_MODEL = "gemini-4-hydrogen";
export const DEFAULT_GEMINI_API_BASE_URL = "https://generativelanguage.googleapis.com";
export const ALLOWED_ATTACHMENT_BUCKET = "teaching-attachments-v1";
export const MAX_EXTRACTION_PAGES = 6;
export const MAX_PAGE_BYTES = 6 * 1024 * 1024;
export const MAX_TOTAL_IMAGE_BYTES = 12 * 1024 * 1024;
export const GEMINI_REQUEST_TIMEOUT_MS = 45_000;
