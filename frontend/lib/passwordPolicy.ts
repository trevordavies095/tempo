/** Mirrors `PasswordPolicy` in the API for pre-submit checks (length + UTF-8 byte cap). */

export const PASSWORD_MIN_LENGTH = 16;
export const PASSWORD_MAX_LENGTH = 64;
export const PASSWORD_MAX_UTF8_BYTES = 72;

function utf8ByteLength(value: string): number {
  return new TextEncoder().encode(value).length;
}

/**
 * Validates length and UTF-8 byte cap. Blocklist, username, and repetition rules are enforced server-side only.
 */
export function getPasswordLengthAndBytesError(password: string): string | null {
  if (!password.trim()) {
    return 'Password is required';
  }

  if (password.length < PASSWORD_MIN_LENGTH) {
    return `Password must be at least ${PASSWORD_MIN_LENGTH} characters`;
  }

  if (password.length > PASSWORD_MAX_LENGTH) {
    return `Password must be at most ${PASSWORD_MAX_LENGTH} characters`;
  }

  if (utf8ByteLength(password) > PASSWORD_MAX_UTF8_BYTES) {
    return 'Password is too long when encoded (maximum 72 bytes in UTF-8); use a shorter passphrase';
  }

  return null;
}
