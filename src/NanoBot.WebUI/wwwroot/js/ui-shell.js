(function () {
  const CLASS_NAME = "nb-scrolled";
  const THRESHOLD = 8;

  function setScrolled(scrolled) {
    document.body.classList.toggle(CLASS_NAME, scrolled);
  }

  function evaluate() {
    const mainContent = document.querySelector(".mud-main-content");
    const scrolled = (mainContent && mainContent.scrollTop > THRESHOLD) || window.scrollY > THRESHOLD;
    setScrolled(!!scrolled);
  }

  function bind() {
    window.addEventListener("scroll", evaluate, { passive: true });

    const mainContent = document.querySelector(".mud-main-content");
    if (mainContent) {
      mainContent.addEventListener("scroll", evaluate, { passive: true });
    }

    evaluate();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", bind, { once: true });
  } else {
    bind();
  }
})();
