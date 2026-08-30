export function setCookie(tz) {
  document.cookie = 'tz=' + encodeURIComponent(tz) + ';path=/;max-age=31536000;samesite=lax';
}

export function ensureCookie() {
  var hasCookie = document.cookie.split('; ').some(function (row) { return row.startsWith('tz='); });
  if (hasCookie) {
    return false;
  }
  try {
    var tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
    document.cookie = 'tz=' + encodeURIComponent(tz) + ';path=/;max-age=31536000;samesite=lax';
    return true;
  } catch (e) {
    return false;
  }
}
