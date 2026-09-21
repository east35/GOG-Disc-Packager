// Image-only PDF: preserve the canvas composition and explicit physical page sizes.
// Pages arrive lazily so high-resolution canvases are released one at a time.
export function createPdf(pages) {
  const encode = (s) => new TextEncoder().encode(s);
  const objects = [null, null];
  const kids = [];
  for (const { canvas, widthMm, heightMm } of pages) {
    const pageId = objects.length + 1, imageId = pageId + 1, contentId = pageId + 2;
    const w = (widthMm * 72 / 25.4).toFixed(4), h = (heightMm * 72 / 25.4).toFixed(4);
    // Composite transparency on white, including the spindle hole.
    const ctx = canvas.getContext('2d');
    ctx.save(); ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.globalCompositeOperation = 'destination-over';
    ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, canvas.width, canvas.height); ctx.restore();
    const jpeg = Uint8Array.from(atob(canvas.toDataURL('image/jpeg', 0.98).split(',')[1]), c => c.charCodeAt(0));
    const content = `q ${w} 0 0 ${h} 0 0 cm /Artwork Do Q\n`;
    kids.push(`${pageId} 0 R`);
    objects.push([encode(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${w} ${h}] /Resources << /XObject << /Artwork ${imageId} 0 R >> >> /Contents ${contentId} 0 R >>`)]);
    objects.push([encode(`<< /Type /XObject /Subtype /Image /Width ${canvas.width} /Height ${canvas.height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${jpeg.length} >>\nstream\n`), jpeg, encode('\nendstream')]);
    objects.push([encode(`<< /Length ${encode(content).length} >>\nstream\n${content}endstream`)]);
    canvas.width = canvas.height = 1;
  }
  if (!kids.length) throw Error('No artwork pages to export.');
  objects[0] = [encode('<< /Type /Catalog /Pages 2 0 R >>')];
  objects[1] = [encode(`<< /Type /Pages /Count ${kids.length} /Kids [${kids.join(' ')}] >>`)];
  const chunks = [encode('%PDF-1.4\n')], offsets = [0];
  let size = chunks[0].length;
  const append = (bytes) => { chunks.push(bytes); size += bytes.length; };
  objects.forEach((parts, index) => {
    offsets.push(size);
    append(encode(`${index + 1} 0 obj\n`));
    parts.forEach(append);
    append(encode('\nendobj\n'));
  });
  const xref = size;
  append(encode(`xref\n0 ${objects.length + 1}\n0000000000 65535 f \n${offsets.slice(1).map(n => `${String(n).padStart(10, '0')} 00000 n \n`).join('')}trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`));
  return new Blob(chunks, { type: 'application/pdf' });
}
