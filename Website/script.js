const yearNode = document.getElementById("year");

if (yearNode) {
  yearNode.textContent = new Date().getFullYear();
}

const counters = document.querySelectorAll("[data-count]");

const runCounter = (node) => {
  const target = Number(node.getAttribute("data-count")) || 0;
  const duration = 1100;
  const start = performance.now();

  const tick = (now) => {
    const progress = Math.min((now - start) / duration, 1);
    const eased = 1 - Math.pow(1 - progress, 3);
    node.textContent = Math.round(target * eased).toString();

    if (progress < 1) {
      requestAnimationFrame(tick);
    }
  };

  requestAnimationFrame(tick);
};

const observer = new IntersectionObserver((entries) => {
  entries.forEach((entry) => {
    if (!entry.isIntersecting) {
      return;
    }

    runCounter(entry.target);
    observer.unobserve(entry.target);
  });
}, { threshold: 0.7 });

counters.forEach((counter) => observer.observe(counter));
