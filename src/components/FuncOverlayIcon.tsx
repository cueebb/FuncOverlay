import React from 'react';

interface FuncOverlayIconProps {
  className?: string;
  size?: number;
  variant?: 'black-on-white' | 'white-on-black';
}

export const FuncOverlayIcon: React.FC<FuncOverlayIconProps> = ({
  className = "w-4 h-4",
  size,
  variant = 'white-on-black',
}) => {
  const isWhiteOnBlack = variant === 'white-on-black';

  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 32 32"
      fill="none"
      width={size}
      height={size}
      className={className}
      aria-label="FuncOverlay Icon"
    >
      {/* Base Icon Background */}
      <rect
        x="2"
        y="2"
        width="28"
        height="28"
        rx="6"
        fill={isWhiteOnBlack ? "#09090B" : "#FFFFFF"}
        stroke={isWhiteOnBlack ? "#FFFFFF" : "#000000"}
        strokeWidth="1.5"
      />

      {/* Layer 1: Underlay Screen Frame (Translucent Layer) */}
      <rect
        x="6"
        y="6"
        width="14"
        height="14"
        rx="2.5"
        stroke={isWhiteOnBlack ? "rgba(255,255,255,0.35)" : "rgba(0,0,0,0.35)"}
        strokeWidth="1.25"
        strokeDasharray="2 1.5"
      />

      {/* Layer 2: Primary Foreground Overlay (Active Sheet) */}
      <rect
        x="11"
        y="11"
        width="15"
        height="15"
        rx="3"
        fill={isWhiteOnBlack ? "#18181B" : "#F4F4F5"}
        stroke={isWhiteOnBlack ? "#FFFFFF" : "#000000"}
        strokeWidth="1.5"
      />

      {/* Stylized Function Glyph / Overlay Crosshair Accent inside active sheet */}
      <path
        d="M15 15.5H22M15 19H20M15 14V23"
        stroke={isWhiteOnBlack ? "#FFFFFF" : "#000000"}
        strokeWidth="1.5"
        strokeLinecap="round"
      />

      {/* Modern Accent Dot */}
      <circle
        cx="23.5"
        cy="8.5"
        r="1.25"
        fill={isWhiteOnBlack ? "#FFFFFF" : "#000000"}
      />
    </svg>
  );
};
